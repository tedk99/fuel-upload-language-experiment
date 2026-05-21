# F# Fuel Engine — Agent Guide

Language: F# / .NET 10.0  
Build: `dotnet test FSharpFuelEngine.slnx`  
Format: `dotnet format FSharpFuelEngine.slnx --verify-no-changes --verbosity minimal`  
Tests: 20 NUnit tests  

See `../CLAUDE.md` for the domain problem, shared rules, and cross-language vocabulary.

---

## Module map

F# compilation order is significant — files must appear in dependency order in the `.fsproj`.

```
FuelUpload.Domain/
  Primitives.fs     — Branded single-case DUs (RowNumber, VehicleIdentifier, TransactionId, etc.)
                      and base enums (FuelKind)
  Vehicle.fs        — Vehicle record, VehicleLookupResult DU (Found/NotFound/Ambiguous/Unavailable)
  FuelRow.fs        — ParsedFuelRow input record
  Duplicate.fs      — DuplicateCheckResult DU, PreviousAttemptState DU
  Validation.fs     — ValidationError DU, Warning DU, ValidationConfig record
  Decision.fs       — RowDecision DU, AcceptedTransaction, QuarantinedRow, QuarantineReasons
                      (non-empty wrapper), RejectedRow, SkippedDuplicate, FatalProcessingError
  BatchSummary.fs   — BatchSummary record, BatchDecision DU, UploadMode DU
  DecisionEngine.fs — classifyRow, classifyBatch (pure domain logic; no I/O)
  Interop.fs        — DTO records, toDomainRequest, toResponseDto, FuelUploadFacade class

FuelUpload.Domain.Tests/
  Tests.fs          — NUnit tests
```

---

## Key types

### RowDecision (discriminated union)

```fsharp
[<RequireQualifiedAccess>]
type RowDecision =
    | Accepted            of AcceptedTransaction
    | AcceptedWithWarnings of AcceptedTransaction * Warning list
    | Quarantined         of QuarantinedRow
    | SkippedDuplicate    of SkippedDuplicate
    | Rejected            of RejectedRow
    | Fatal               of FatalProcessingError
```

`RequireQualifiedAccess` means all uses must be prefixed: `RowDecision.Accepted`, etc.

### UploadMode (discriminated union)

```fsharp
type UploadMode = Normal | Retry | ConservativeRecovery | AggressiveRecovery
```

### PreviousAttemptState (discriminated union)

```fsharp
type PreviousAttemptState =
    | NotRetryable
    | Retryable
    | Canonicalized
    | FailedBeforeCanonicalisation
    | FailedBeforeFinalization
```

This single union replaces independent boolean fields for canonicalization/finalization state.

### QuarantineReasons (non-empty wrapper)

```fsharp
type QuarantineReasons = private QuarantineReasons of QuarantineReason list

module QuarantineReasons =
    val create : QuarantineReason list -> QuarantineReasons option  // None if empty
    val toList : QuarantineReasons -> QuarantineReason list
```

The private constructor prevents constructing an empty reasons list at the type level.

### VehicleLookupResult (discriminated union)

```fsharp
type VehicleLookupResult =
    | Found   of Vehicle
    | NotFound
    | Ambiguous
    | Unavailable of string  // service error message
```

---

## Public API

```fsharp
// Domain (DecisionEngine module)
val classifyRow :
    ValidationConfig -> UploadMode -> ParsedFuelRow
    -> VehicleLookupResult -> DuplicateCheckResult
    -> RowDecision

val classifyBatch :
    ValidationConfig -> UploadMode -> FuelRowContext seq
    -> BatchDecision

// Application boundary (Interop module)
type FuelUploadFacade =
    static member Classify :
        FuelUploadRequestDto -> Result<FuelUploadResponseDto, FuelUploadMappingError list>
```

---

## Classification flow

```
FuelUploadFacade.Classify
  → toDomainRequest            (DTO strings → ValidationConfig, UploadMode, FuelRowContext list)
  → classifyBatch
      → classifyRow (per row)
          → validate           (accumulate ValidationErrors)
          → VehicleLookupResult match
          → duplicate policy   (PreviousAttemptState determines skip/accept)
          → quarantine check   (suspicious merchant/quantity/cost patterns)
          → build AcceptedTransaction + warnings
  → toResponseDto              (BatchDecision → FuelUploadResponseDto)
```

---

## Interop notes

`[<CLIMutable>]` is applied to DTO records so C# callers can construct them via property setters and object initializers. This weakens the F# model when types cross the boundary — a `[<CLIMutable>]` record can be constructed with null or default values from C#.

Domain records (`AcceptedTransaction`, `QuarantinedRow`, etc.) also carry `[<CLIMutable>]` for interop convenience. If consuming this from F# only, those attributes are unnecessary.

---

## Limitations

- `[<CLIMutable>]` on domain records allows C# to construct invalid domain values (null fields, wrong defaults).
- DTO response uses `%A` (F# default formatting) for some domain values — not production-ready serialization.
- Duplicate state does not carry the previous transaction key, making it less realistic for audit or idempotency scenarios.
- No property tests.
