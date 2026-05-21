# C# Fuel Engine — Agent Guide

Language: C# / .NET 10.0  
Build: `dotnet test FuelUploadEngine.slnx`  
Format: `dotnet format FuelUploadEngine.slnx --verify-no-changes --verbosity minimal`  
Tests: 32 xUnit tests  

See `../CLAUDE.md` for the domain problem, shared rules, and cross-language vocabulary.

---

## Module map

```
src/FuelUploadEngine/
  Domain/
    Primitives.cs            — Branded value types: RowNumber, VehicleIdentifier, VehicleId,
                               ExternalReference, TransactionKey
    RowDecision.cs           — abstract record RowDecision + 6 sealed subrecords (see below)
    BatchDecision.cs         — BatchDecision (uploadable/blocked) and BatchSummary
    DuplicateCheckResult.cs  — DuplicateCheckResult sealed hierarchy: NoDuplicate, DuplicateFound,
                               DuplicateCheckFailed
    FuelRow.cs               — FuelRow input record
    PreviousUploadOutcome.cs — PreviousUploadOutcome sealed hierarchy
    Validation.cs            — ValidationError, ValidationConfig, UploadWarning enums/records
    Vehicle.cs               — Vehicle record, VehicleLookupResult sealed hierarchy

  Engine/
    FuelUploadDecisionEngine.cs  — ClassifyRow / ClassifyBatch: main classification logic
    DuplicatePolicy.cs           — Classifies duplicate rows per UploadMode;
                                   returns RowDecision? (null = not a duplicate path)
    QuarantinePolicy.cs          — Detects suspicious patterns; returns QuarantinedRow or null
    FuelRowValidator.cs          — Accumulates ValidationErrors and UploadWarnings from a FuelRow
    TransactionFactory.cs        — Builds FuelTransaction from a validated row
    WarningPolicy.cs             — Detects threshold-exceeded UploadWarnings
    BatchSummaryCalculator.cs    — Derives BatchSummary by reducing over List<RowDecision>

  Application/
    FuelUploadDtos.cs            — FuelUploadRequestDto, FuelUploadResponseDto, FuelUploadDecisionDto
    FuelUploadMapper.cs          — DTO → domain and domain → DTO conversion;
                                   returns FuelUploadMapResult<T>
    FuelUploadApplicationService.cs — Public entry point: Classify(FuelUploadRequestDto)

tests/FuelUploadEngine.Tests/
  — xUnit tests: decision engine, boundary mapping, quarantine policy, batch summary
```

---

## Key types

### RowDecision (abstract sealed record hierarchy)

```csharp
abstract record RowDecision;

sealed record AcceptedTransaction(
    FuelTransaction Transaction) : RowDecision;

sealed record AcceptedTransactionWithWarnings(
    FuelTransaction Transaction,
    IReadOnlyList<UploadWarning> Warnings) : RowDecision;

sealed record QuarantinedRow(
    RowNumber RowNumber,
    FuelTransaction Transaction,
    IReadOnlyList<QuarantineReason> Reasons,   // checked at runtime: must be non-empty
    IReadOnlyList<UploadWarning> Warnings) : RowDecision;

sealed record SkippedDuplicate(
    RowNumber RowNumber,
    DuplicateState Duplicate,
    DuplicateSkipCode Reason) : RowDecision;

sealed record RejectedRow(
    RowNumber RowNumber,
    RejectionReason Reason) : RowDecision;

sealed record FatalProcessingError(
    RowNumber RowNumber,
    FatalError Error) : RowDecision;
```

### UploadMode (enum)

```csharp
enum UploadMode { Normal, Retry, ConservativeRecovery, AggressiveRecovery }
```

### Key enums

- `ValidationErrorCode`: MissingVehicleIdentifier, NonPositiveQuantity, QuantityExceedsMaximum, NegativeUnitPrice, UnitPriceExceedsMaximum, TransactionDateInFuture
- `WarningCode`: QuantityAboveWarningThreshold, UnitPriceAboveWarningThreshold
- `QuarantineReasonCode`: SuspiciousMerchantName, SuspiciousQuantityPattern, SuspiciousCostPattern
- `DuplicateSkipCode`: DuplicateInNormalMode, PreviousAttemptNotRetryable, PreviousAttemptAlreadyCanonicalized
- `RejectionCode`: ValidationFailed, VehicleNotFound, AmbiguousVehicle
- `FatalErrorCode`: VehicleLookupUnavailable, DuplicateCheckUnavailable

---

## Public API

```csharp
// Domain (Engine layer)
RowDecision FuelUploadDecisionEngine.ClassifyRow(
    FuelRow row,
    VehicleLookupResult vehicleLookup,
    DuplicateCheckResult duplicateCheck,
    ValidationConfig validationConfig,
    UploadMode mode)

BatchDecision FuelUploadDecisionEngine.ClassifyBatch(BatchClassificationRequest request)

// Application boundary (DTO layer)
FuelUploadMapResult<FuelUploadResponseDto> FuelUploadApplicationService.Classify(
    FuelUploadRequestDto request)
```

`FuelUploadMapResult<T>` is either a success with `T` or a list of `FuelUploadMappingError`.

---

## Classification flow

```
FuelUploadApplicationService.Classify
  → FuelUploadMapper.ToDomainRequest        (DTO → domain types)
  → FuelUploadDecisionEngine.ClassifyBatch
      → ClassifyRow (per row)
          → FuelRowValidator                (accumulate errors/warnings)
          → VehicleLookupResult switch      (fatal / not-found / ambiguous / found)
          → DuplicatePolicy.ClassifyDuplicate (returns RowDecision? or null)
          → QuarantinePolicy                (check suspicious patterns)
          → TransactionFactory / WarningPolicy
      → BatchSummaryCalculator.Calculate
  → FuelUploadMapper.ToResponseDto          (domain → DTO)
```

---

## Limitations

- Switch expressions require `default throw` branches — adding a new `RowDecision` subtype or `UploadMode` value will not cause compile errors at unhandled sites.
- `QuarantinedRow` reasons are checked non-empty at runtime (not by the type system).
- DTO outcome strings in the response mapper are literal string values, not enums.
- `DuplicateState` cross-field combinations (e.g. a pre-canonical failure with a canonical key) are not prevented by types.
