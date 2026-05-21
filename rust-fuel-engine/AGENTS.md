# Rust Fuel Engine — Agent Guide

Language: Rust 1.95.0, 2024 edition  
Build: `cargo fmt --check && cargo test && cargo clippy --all-targets -- -D warnings`  
Tests: 23 tests across 3 test files  

See `../CLAUDE.md` for the domain problem, shared rules, and cross-language vocabulary.

---

## Module map

```
src/
  lib.rs            — Crate root; re-exports: pub use boundary::*, domain::*, engine::{classify_batch, classify_row}

  boundary.rs       — FuelUploadRequestDto, FuelUploadResponseDto, FuelUploadDecisionDto,
                      FuelUploadMappingError, FuelUploadApplicationService::classify

  domain/
    mod.rs          — Re-exports all domain types
    primitives.rs   — Branded newtypes: RowNumber, VehicleId, SourceRowId, FuelDate, Merchant,
                      OdometerReading
    vehicle.rs      — Vehicle struct, VehicleLookupResult enum
    row.rs          — ParsedFuelRow input struct
    duplicate.rs    — DuplicateCheckResult enum, DuplicateState enum, UploadMode enum
    validation.rs   — ValidationError enum, Warning enum, ValidationConfig struct
    decision.rs     — RowDecision enum, FuelTransaction, RejectedRow, SkippedDuplicate,
                      QuarantineReasons (non-empty wrapper), Warning, Summary, BatchDecision

  engine/
    mod.rs               — Re-exports classify_batch, classify_row
    classifier.rs        — classify_row, classify_batch (main logic)
    duplicate_policy.rs  — classify_duplicate: determines skip reason per UploadMode and DuplicateState
    transaction_factory.rs — build_transaction: constructs FuelTransaction from valid inputs
    summary.rs           — derive_summary: folds &[RowDecision] → Summary

tests/
  decision_engine_tests.rs  — Main classification tests (accepted, rejected, quarantine, fatal)
  boundary_tests.rs         — DTO mapping tests (to-domain and to-response)
  duplicate_policy_tests.rs — Duplicate policy per mode
```

---

## Key types

### RowDecision (enum)

```rust
pub enum RowDecision {
    Accepted(FuelTransaction),
    Warning {
        transaction: FuelTransaction,
        warnings: Vec<Warning>,          // non-empty by convention, not enforced by type
    },
    Quarantined {
        transaction: FuelTransaction,
        reasons: QuarantineReasons,      // non-empty wrapper type
        warnings: Vec<Warning>,
    },
    SkippedDuplicate(SkippedDuplicate),
    Rejected(RejectedRow),
    Fatal(FatalError),
}
```

Note: The `Warning` variant in Rust corresponds to `AcceptedWithWarnings` in other implementations.

### UploadMode (enum)

```rust
pub enum UploadMode { Normal, Retry, ConservativeRecovery, AggressiveRecovery }
```

### BatchDecision (enum)

```rust
pub enum BatchDecision {
    Ready   { rows: Vec<RowDecision>, summary: Summary },
    Blocked { rows: Vec<RowDecision>, summary: Summary, fatal_errors: Vec<FatalError> },
}
```

### QuarantineReasons (non-empty wrapper)

```rust
pub struct QuarantineReasons(Vec<QuarantineReason>);
// Constructed only via checked constructor; prevents empty reason list
```

### VehicleLookupResult (enum)

```rust
pub enum VehicleLookupResult {
    Found(Vehicle),
    NotFound,
    Ambiguous(Vec<VehicleId>),
    Unavailable(String),
}
```

### DuplicateState (enum)

```rust
pub enum DuplicateState {
    NoDuplicate,
    Duplicate { /* canonicalization and finalization state fields */ },
    CheckFailed(String),
}
```

---

## Public API

```rust
// Domain (engine module)
pub fn classify_row(
    input: &RowInput,
    mode: UploadMode,
    config: &ValidationConfig,
) -> RowDecision

pub fn classify_batch(
    inputs: &[RowInput],
    mode: UploadMode,
    config: &ValidationConfig,
) -> BatchDecision

// Application boundary
impl FuelUploadApplicationService {
    pub fn classify(
        request: &FuelUploadRequestDto,
    ) -> Result<FuelUploadResponseDto, Vec<FuelUploadMappingError>>
}
```

---

## Classification flow

```
FuelUploadApplicationService::classify
  → parse DTO (boundary.rs)   (strings → UploadMode, ValidationConfig, Vec<RowInput>)
  → classify_batch
      → classify_row (per RowInput)
          → validate_row       (accumulate ValidationErrors and Warnings)
          → VehicleLookupResult match (exhaustive; Unavailable → Fatal)
          → duplicate_policy::classify_duplicate (returns Option<SkippedDuplicate>)
          → quarantine check   (suspicious patterns → QuarantineReasons or None)
          → transaction_factory::build_transaction
      → derive_summary         (fold &[RowDecision] → Summary)
  → map to response DTO        (boundary.rs)
```

---

## Rust-specific notes

- All `match` on enums is exhaustive — the compiler enforces that every variant is handled. Adding a new `RowDecision` variant or `UploadMode` case will cause compile errors at every unhandled `match` site.
- `clippy --all-targets -- -D warnings` is part of the required build check; treat clippy warnings as errors.
- Newtypes (`RowNumber`, `VehicleId`, etc.) prevent accidental mixing of primitive identifiers.
- No `null` — optionality is always `Option<T>`.

---

## Limitations

- Numeric fields (`quantity_liters`, `total_cost`, `unit_cost`) use `f64` — precision risk for financial/fuel values despite finite-value checks in validation.
- Warning `Vec` in the `Warning` variant can be empty in public API — non-empty is not enforced by type (unlike `QuarantineReasons`).
- `DuplicateState` cross-field combinations (e.g. inconsistent canonicalization + finalization flags) are not prevented by types.
- DTO response uses debug formatting (`{:?}`) for some reason strings.
- No property tests.
