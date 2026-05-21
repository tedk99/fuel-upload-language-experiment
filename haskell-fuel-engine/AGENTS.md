# Haskell Fuel Engine — Agent Guide

Language: Haskell (GHC 9.6.7), Cabal 3.14.2  
Build: `cabal test all` (run from this directory)  
Tests: 28 Hspec examples + 4 QuickCheck property tests  

See `../CLAUDE.md` for the domain problem, shared rules, and cross-language vocabulary.

This is the **reference implementation**. It has the strongest algebraic model, the only property tests, and is used as a correctness oracle when cross-language behaviour is in question. It is not a practical production choice for a C#/.NET shop.

---

## Module map

```
src/FuelUpload/
  Domain/
    Primitive.hs    — Branded newtypes: RowNumber, VehicleId, ExternalRowId, Registration,
                      TransactionId, FuelQuantity, MoneyAmount, OdometerReading, MerchantName
    Vehicle.hs      — Vehicle record, VehicleLookupResult ADT
                      (Found Vehicle | NotFound Registration | Unavailable String)
    Row.hs          — ParsedFuelRow input record, RowContext (row + lookup + duplicate)
    Duplicate.hs    — DuplicateCheckResult, PreviousAttempt record, UploadMode ADT
    Decision.hs     — RowDecision, FuelTransaction, ValidationError, ValidationWarning,
                      QuarantineReason, RejectionReason, DuplicateSkipReason,
                      BatchDecision, BatchSummary, BatchOutcome

  Validation.hs     — validateRow :: ValidationConfig -> ParsedFuelRow
                        -> Either (NonEmpty ValidationError) [ValidationWarning]
  DuplicatePolicy.hs — classifyDuplicate :: UploadMode -> PreviousAttempt -> DuplicateSkipReason?
  Summary.hs        — deriveSummary :: [RowDecision] -> BatchSummary
  DecisionEngine.hs — classifyRow :: ValidationConfig -> UploadMode -> RowContext -> RowDecision
                      classifyBatch :: ValidationConfig -> UploadMode -> [RowContext] -> BatchDecision
  Api.hs            — DTO types, toDomainRequest, toResponseDto, classifyUploadDto

test/FuelUpload/
  DecisionEngineSpec.hs — 28 Hspec examples (normal, retry, recovery, quarantine, fatal)
  Properties.hs          — 4 QuickCheck properties (accepted rows have no errors, etc.)
```

---

## Key types

### RowDecision (sum type)

```haskell
data RowDecision
  = Accepted            FuelTransaction
  | AcceptedWithWarnings FuelTransaction (NonEmpty ValidationWarning)
  | Quarantined          FuelTransaction (NonEmpty QuarantineReason)
  | SkippedDuplicate     SkippedDuplicateInfo
  | Rejected             RejectedRow
  | Fatal                FatalError
```

`NonEmpty` (from `Data.List.NonEmpty`) enforces at compile time that warnings, quarantine reasons, and validation errors cannot be empty.

### UploadMode (sum type)

```haskell
data UploadMode = Normal | Retry | ConservativeRecovery | AggressiveRecovery
```

### BatchOutcome (sum type)

```haskell
data BatchOutcome
  = BatchUploadable
  | BatchBlockedByFatal (NonEmpty FatalError)
```

### ValidationError (sum type — carries values)

```haskell
data ValidationError
  = QuantityMustBePositive     FuelQuantity
  | AmountMustBePositive       MoneyAmount
  | OdometerMustNotBeNegative  OdometerReading
  | QuantityExceedsMaximum     FuelQuantity FuelQuantity   -- actual, maximum
  | AmountExceedsMaximum       MoneyAmount  MoneyAmount
```

### RejectionReason (sum type)

```haskell
data RejectionReason
  = VehicleWasNotFound       Registration
  | RowFailedValidation      (NonEmpty ValidationError)
  | DuplicateCannotBeUploaded UploadMode DuplicateSkipReason
```

### NonEmpty usage — where compile-time non-empty guarantees apply

| Type | Where |
|---|---|
| `NonEmpty ValidationWarning` | `AcceptedWithWarnings` constructor |
| `NonEmpty QuarantineReason` | `Quarantined` constructor |
| `NonEmpty ValidationError` | `RowFailedValidation` in RejectionReason |
| `NonEmpty FatalError` | `BatchBlockedByFatal` in BatchOutcome |

---

## Public API

```haskell
-- Domain (DecisionEngine)
classifyRow   :: ValidationConfig -> UploadMode -> RowContext -> RowDecision
classifyBatch :: ValidationConfig -> UploadMode -> [RowContext] -> BatchDecision

-- Application boundary (Api)
classifyUploadDto :: FuelUploadRequestDto -> Either [FuelUploadMappingError] FuelUploadResponseDto

toDomainRequest :: FuelUploadRequestDto -> Either [FuelUploadMappingError] DomainUploadRequest
toResponseDto   :: BatchDecision -> FuelUploadResponseDto
```

---

## Classification flow

```
classifyUploadDto
  → toDomainRequest       (DTO strings → ValidationConfig, UploadMode, [RowContext])
  → classifyBatch
      → classifyRow (per RowContext)
          → validateRow   (Either (NonEmpty ValidationError) [ValidationWarning])
          → VehicleLookupResult case
          → classifyDuplicate (DuplicatePolicy; produces DuplicateSkipReason or Nothing)
          → quarantine check (suspicious patterns → NonEmpty QuarantineReason or Nothing)
          → build Accepted / AcceptedWithWarnings / Quarantined
  → deriveSummary         (fold [RowDecision] → BatchSummary)
  → toResponseDto
```

---

## Limitations

- `PreviousAttempt` is a record with independent `canonicalizationState` and `finalizationState` fields. Cross-field combinations (e.g. failed-before-canonicalization + finalized) can be constructed — these are not prevented by types.
- `skipReasonForPreviousAttempt` can produce imprecise skip reasons for some `PreviousAttempt` combinations.
- Vehicle lookup does not have an `Ambiguous` case (present in C#, F#, and Rust); the Haskell model uses `NotFound` for this.
- DTO boundary uses `String` for many fields and `show` for response values — not production-ready serialization.
- No formatter/linter equivalent to `cargo clippy` was run in V2.
- Practical fit for a C#/.NET shop is low — different toolchain, ecosystem, and hiring assumptions.
