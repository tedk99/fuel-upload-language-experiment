## 1. What Is V3?

V3 was the "integration pressure" round. V1/V2 proved the engines could model the pure fuel-upload decision rules and evolve domain outcomes safely. V3 asked a more production-like question: can the clean domain survive CSV-shaped imports, audit/event outputs, repository-style lookups, and operational reporting without letting raw strings, IO, persistence, or duplicate summary logic leak into the core?

V3 was trying to solve the problem V1/V2 mostly avoided: real systems do not just classify already-clean rows. They receive messy import records, call lookup services, need audit trails, and produce business reports. V3 tested whether each implementation could keep those concerns at the boundary.

Where it lives:

- Branch: `v3`
- Main active dirs:
  - `csharp-fuel-engine`
  - `fsharp-fuel-engine`
  - `haskell-fuel-engine`
  - `rust-fuel-engine`
  - `docs`
- Frozen baseline dirs:
  - `typescript-fuel-engine`
  - `purescript-fuel-engine`

Commits after v2:

```text
c7c86a9 docs: add v3 integration pressure experiment plan
1bd14af feat: add csv-shaped import boundary for target languages
ef68002 feat: add audit projection for target languages
655a2e7 plans
f91e814 feat: add repository ports for target language boundaries
d6ec9b2 feat: add operational batch report projection
3cc071b docs: score v3 integration pressure experiment
54debc3 docs: add F# learning guide
```

The v3 scoring/eval docs are:

- `docs/v3-results.md`
- `docs/v3-scoring-rubric.md`
- `docs/v3-experiment-plan.md`
- `docs/fsharp-learning-guide.md`

The v2 equivalent in this repo is `docs/v2-results.md`, not `docs/v2-language-evolution-experiment-score.md`.

## 2. The New Reference Implementation

Short version: there is not a new sixth engine.

The v3 result says:

- F# is now the best core-plus-boundary compromise for a .NET team.
- Haskell is still the best reference-only correctness model.
- C# is still the most practical all-C# production shape.

For the book, I would treat F# as the new teaching reference for "what we would actually want a .NET team to learn from." It is not ground truth that the others are scored against. It is one of the evolved engines, now documented as the canonical learning path in `docs/fsharp-learning-guide.md`.

It lives here:

- `fsharp-fuel-engine/FuelUpload.Domain`
- tests in `fsharp-fuel-engine/FuelUpload.Domain.Tests/Tests.fs`

Architecture: F# has a typed domain core built around records and discriminated unions. Boundary inputs are string-heavy DTOs in `Interop.fs`; those map into typed `ValidationConfig`, `UploadMode`, `FuelRowContext`, `VehicleLookupResult`, and `DuplicateCheckResult`. The pure decision engine in `DecisionEngine.fs` classifies rows into `RowDecision` cases. Batch summaries are derived from decisions. CSV imports, repository ports, audit projection, and operational reports all sit outside the core. Recovery is modeled through `UploadMode` plus `PreviousAttemptState`, including conservative and aggressive recovery modes.

Representative snippets:

```fsharp
// fsharp-fuel-engine/FuelUpload.Domain/Decision.fs

[<RequireQualifiedAccess>]
type RowDecision =
    | Accepted of AcceptedTransaction
    | AcceptedWithWarnings of AcceptedTransaction * Warning list
    | Quarantined of QuarantinedRow
    | SkippedDuplicate of SkippedDuplicate
    | Rejected of RejectedRow
    | Fatal of FatalProcessingError

[<CLIMutable>]
type FuelRowContext =
    { Row: ParsedFuelRow
      VehicleLookup: VehicleLookupResult
      DuplicateCheck: DuplicateCheckResult }

[<CLIMutable>]
type ClassifiedRow =
    { Row: ParsedFuelRow
      Decision: RowDecision }
```

Why this matters: accepted, warning, quarantine, skip, rejection, and fatal are explicit cases, not strings.

```fsharp
// fsharp-fuel-engine/FuelUpload.Domain/DecisionEngine.fs

let private accepted config mode row vehicle =
    let transaction = toTransaction mode row vehicle
    let warnings = Validation.warningsFor config row

    match Validation.quarantineReasonsFor config row |> QuarantineReasons.create with
    | Some reasons ->
        RowDecision.Quarantined
            { Transaction = transaction
              Reasons = reasons
              Warnings = warnings }
    | None ->
        match warnings with
        | [] -> RowDecision.Accepted transaction
        | warnings -> RowDecision.AcceptedWithWarnings(transaction, warnings)
```

Why this matters: warnings and quarantine are distinct, and quarantine reasons are non-empty through `QuarantineReasons.create`.

```fsharp
// fsharp-fuel-engine/FuelUpload.Domain/DecisionEngine.fs

match mode, duplicateCheck with
| _, DuplicateCheckResult.NoDuplicate -> accepted config mode row vehicle
| UploadMode.Normal, DuplicateCheckResult.Duplicate previous ->
    skipped row mode previous DuplicateSkipReason.NormalModeDuplicate
| UploadMode.Retry, DuplicateCheckResult.Duplicate PreviousAttemptState.RetryableFailure ->
    accepted config mode row vehicle
| UploadMode.ConservativeRecovery,
  DuplicateCheckResult.Duplicate PreviousAttemptState.FailedBeforeCanonicalFinalization ->
    accepted config mode row vehicle
| UploadMode.AggressiveRecovery,
  DuplicateCheckResult.Duplicate PreviousAttemptState.FailedAfterCanonicalizationWithoutCanonicalTransactionKey ->
    accepted config mode row vehicle
| UploadMode.AggressiveRecovery, DuplicateCheckResult.Duplicate previous ->
    skipped row mode previous (DuplicateSkipReason.RecoveryModeDuplicateAlreadyCanonicalized previous)
```

Why this matters: the recovery matrix is visible and typed. The missing aggressive recovery branch from the footgun catalogue is explicitly represented.

```fsharp
// fsharp-fuel-engine/FuelUpload.Domain/Interop.fs

[<RequireQualifiedAccess>]
type FuelImportErrorCode =
    | MissingRows
    | MissingRequiredCell
    | InvalidNumber
    | InvalidDate
    | InvalidUploadMode
    | InvalidBoolean

type IVehicleRepository =
    abstract Lookup: vehicleKey: string -> Result<VehicleLookupResult, VehicleRepositoryError>

type IDuplicateRepository =
    abstract Lookup: lookup: DuplicateRepositoryLookup -> Result<DuplicateCheckResult, DuplicateRepositoryError>
```

Why this matters: import failures and repository failures are typed boundary concepts, not domain strings.

```fsharp
// fsharp-fuel-engine/FuelUpload.Domain/OperationalBatchReport.fs

let project decision =
    let rows = rowsOf decision
    let status = statusOf decision

    let uploadedTransactionIds =
        if status = OperationalBatchStatus.Fatal then
            []
        else
            rows
            |> List.choose (fun classified ->
                match classified.Decision with
                | RowDecision.Accepted transaction
                | RowDecision.AcceptedWithWarnings(transaction, _) -> Some transaction.TransactionId
                | RowDecision.Quarantined _
                | RowDecision.SkippedDuplicate _
                | RowDecision.Rejected _
                | RowDecision.Fatal _ -> None)
```

Why this matters: the report is derived from decisions and suppresses uploaded IDs for fatal batches.

Tests: F# has its own tests in `fsharp-fuel-engine/FuelUpload.Domain.Tests/Tests.fs`. They test the F# implementation itself: decision rules, boundary mapping, import mapping, audit projection, repository-backed classification, and operational report projection. They are not conformance tests driving other engines.

Final v3 validation for F#:

```text
dotnet test fsharp-fuel-engine/FSharpFuelEngine.slnx
42 tests passed
```

## 3. Deltas To The Existing Five Engines

Important mismatch: the v3 repo I inspected did not contain `normalcsharp-fuel-engine`. It contained `csharp-fuel-engine`, `fsharp-fuel-engine`, `haskell-fuel-engine`, `rust-fuel-engine`, `typescript-fuel-engine`, and `purescript-fuel-engine`. So the notes below distinguish verified v3 changes from normal-junior C# material.

### `csharp-fuel-engine`

Changed in v3.

Added:

- `csharp-fuel-engine/src/FuelUploadEngine/Application/FuelUploadImportDtos.cs`
- `csharp-fuel-engine/src/FuelUploadEngine/Application/FuelUploadImportMapper.cs`
- `csharp-fuel-engine/src/FuelUploadEngine/Application/RepositoryPorts.cs`
- `csharp-fuel-engine/src/FuelUploadEngine/Application/RepositoryFuelUploadApplicationService.cs`
- `csharp-fuel-engine/src/FuelUploadEngine/Domain/Audit.cs`
- `csharp-fuel-engine/src/FuelUploadEngine/Domain/OperationalBatchReport.cs`
- `csharp-fuel-engine/tests/FuelUploadEngine.Tests/AuditProjectionTests.cs`
- `csharp-fuel-engine/tests/FuelUploadEngine.Tests/FuelUploadImportBoundaryTests.cs`
- `csharp-fuel-engine/tests/FuelUploadEngine.Tests/OperationalBatchReportTests.cs`

Modified:

- `csharp-fuel-engine/tests/FuelUploadEngine.Tests/FuelUploadApplicationBoundaryTests.cs`

Behavior added:

- CSV-shaped import boundary
- typed import errors
- audit projection
- repository ports
- repository-backed app service
- operational batch report

Stale-doc risk: any book chapter saying C# only has a pure engine plus DTO boundary is now stale. It now has production-shaped boundaries.

Seven-footgun status: the original seven remain valid for the normal junior C# teaching implementation if that is separate. In idiomatic `csharp-fuel-engine`, v3 further mitigates several of them, but does not erase all boundary slop. Raw string statuses still exist at DTO edges.

### `fsharp-fuel-engine`

Changed heavily in v3.

Added:

- `fsharp-fuel-engine/FuelUpload.Domain/Audit.fs`
- `fsharp-fuel-engine/FuelUpload.Domain/OperationalBatchReport.fs`

Modified:

- `fsharp-fuel-engine/FuelUpload.Domain/Interop.fs`
- `fsharp-fuel-engine/FuelUpload.Domain/FuelUpload.Domain.fsproj`
- `fsharp-fuel-engine/FuelUpload.Domain.Tests/Tests.fs`

Behavior added:

- CSV-shaped import boundary via `ImportedFuelRow`, `ImportBatchRequest`, `FuelImportErrorCode`
- audit projection via `AuditEventKind`, `AuditRecord`, `AuditProjection`
- repository ports via `IVehicleRepository`, `IDuplicateRepository`
- repository-backed facade via `RepositoryFuelUploadFacade`
- operational report via `OperationalBatchReport`

Stale-doc risk: any book material treating F# as "just the cleaner domain core" is now stale. V3 shows F# can handle app-boundary pressure, though `Interop.fs` is now large.

Seven-footgun status: F# mitigates the spirit of most seven footguns at the domain level: no status strings in domain decisions, explicit aggressive recovery, derived summary, typed decisions. But C# interop still permits null/default-ish DTO construction, so do not claim F# magically removes all edge risks.

### `haskell-fuel-engine`

Changed in v3.

Added:

- `haskell-fuel-engine/src/FuelUpload/Audit.hs`
- `haskell-fuel-engine/src/FuelUpload/Report.hs`

Modified:

- `haskell-fuel-engine/src/FuelUpload/Api.hs`
- `haskell-fuel-engine/haskell-fuel-engine.cabal`
- `haskell-fuel-engine/test/FuelUpload/DecisionEngineSpec.hs`

Behavior added:

- CSV-shaped import boundary
- audit projection
- repository-shaped ports
- operational report
- more examples around these additions

Stale-doc risk: any chapter saying Haskell is only a pure/reference core is partly stale. It now has boundary-shaped code, but v3 concluded it still feels more like a correctness oracle than a production app boundary for a C# shop.

Seven-footgun status: Haskell still best demonstrates making errors unrepresentable through ADTs and `NonEmpty`. V3 did not add a cross-engine conformance harness.

### `rust-fuel-engine`

Changed in v3.

Added:

- `rust-fuel-engine/src/domain/audit.rs`
- `rust-fuel-engine/src/domain/report.rs`
- `rust-fuel-engine/tests/audit_tests.rs`
- `rust-fuel-engine/tests/report_tests.rs`

Modified:

- `rust-fuel-engine/src/boundary.rs`
- `rust-fuel-engine/src/domain/decision.rs`
- `rust-fuel-engine/src/domain/mod.rs`
- `rust-fuel-engine/src/engine/transaction_factory.rs`
- `rust-fuel-engine/tests/boundary_tests.rs`

Behavior added:

- CSV-shaped import boundary
- audit projection
- repository traits/service shape
- operational report
- transaction ID support for reports

Stale-doc risk: any chapter saying Rust lacks application-boundary coverage is now stale. It now has a meaningful boundary story, though v3 still notes `f64`, `Vec` payloads, debug strings, and .NET-fit issues.

Seven-footgun status: Rust continues to mitigate nulls, raw domain statuses, and missing match arms well. It still leaves some invariants to tests/convention, especially non-empty vectors and numeric representation.

### `normalcsharp-fuel-engine`

The v3 branch I inspected did not include this folder. In the book repo, `normalcsharp-fuel-engine` appears to be the intentionally normal-junior C# teaching implementation.

For book purposes, the seven-footgun catalogue should remain attached to the normal junior C# implementation. V3 does not invalidate that catalogue; it adds a second lesson: even once the pure domain is improved, integration boundaries can reintroduce slop.

About `docs/01-walkthrough.md` through `docs/05-vs-rust.md`: those files were not present in the v3 repo I inspected, so I could not verify them directly.

## 4. New Material The Book Should Cover

The pedagogical hook of v3:

V1/V2 teach "better types make domain bugs harder." V3 teaches "the bugs come back at the edges." CSV imports, repositories, audit logs, and reports are where teams often reintroduce strings, nulls, mutable counters, and duplicated rules. The lesson is that good modeling has to include the boundary, not just the pure core.

New comparison axes:

- Boundary integrity: did raw CSV/DTO/repository shapes stay outside the domain?
- Typed integration errors: are bad cells and repository failures typed?
- Projection safety: audit/report derived from decisions, not recomputed?
- Practical production fit: can a C#/.NET shop actually live with this?
- Change safety under integration pressure: does adding a new outcome force updates?

V3 diagrams worth using:

- `docs/v3-phase-briefing.md` has Mermaid diagrams for the whole integration flow, CSV boundary, audit projection, repository ports, report projection, and scoring.
- `docs/fsharp-learning-guide.md` has Mermaid diagrams for F# architecture, boundary/domain separation, row-to-decision flow, and decision-to-output flow.

Good book diagram source:

```text
docs/fsharp-learning-guide.md
docs/v3-phase-briefing.md
```

Do NOT include / do not overclaim:

- Do not say F# is a new sixth engine. It is the evolved F# implementation plus a learning guide.
- Do not say F# is mathematically the "ground truth." Haskell still has the strongest reference-only correctness story.
- Do not say TypeScript or PureScript participated in v3. They were frozen baselines.
- Do not claim v3 added real CSV parsing, real DBs, ORMs, HTTP, logging frameworks, or file IO. It intentionally stayed framework-free.
- Do not claim there is a cross-language conformance test suite. Each implementation has its own tests.
- Do not claim v4 is planned or necessary. The current recommendation is to pivot to teaching material.

## 5. Vocabulary

Domain terms mostly did not change from v2 to v3. V3 added boundary/integration vocabulary.

| Concept | Before V3 | V3 / now |
|---|---|---|
| Upload modes | `Normal`, `Retry`, `ConservativeRecovery`, `AggressiveRecovery` | unchanged |
| Previous attempt state | `PreviousAttemptState` | unchanged |
| Row outcome | `RowDecision` | unchanged, now projected into audit/report DTOs |
| CSV/import row | none | `ImportedFuelRow`, `ImportBatchRequest` |
| Import errors | normal mapping errors only | `FuelImportErrorCode` |
| Audit event | none | `AuditEventKind`, `AuditRecord`, `AuditRecordDto` |
| Repository lookup | DTO-provided lookup statuses | `IVehicleRepository`, `IDuplicateRepository` |
| Operational report | response DTO / summary only | `OperationalBatchReport` |
| Operational status | batch ready/blocked | `OperationalBatchStatus.Ready`, `OperationalBatchStatus.Fatal` |

New F# DTO / boundary names:

```text
ImportedFuelRow
ImportBatchRequest
FuelImportErrorCode
FuelImportError
VehicleRepositoryErrorCode
VehicleRepositoryError
DuplicateRepositoryErrorCode
DuplicateRepositoryError
DuplicateRepositoryLookup
IVehicleRepository
IDuplicateRepository
RepositoryFuelUploadFacade
AuditEventKind
AuditRecord
AuditRecordDto
OperationalBatchStatus
OperationalBatchReport
OperationalQuarantinedRow
```

New F# import error codes:

```text
MissingRows
MissingRequiredCell
InvalidNumber
InvalidDate
InvalidUploadMode
InvalidBoolean
```

Audit status text values:

```text
accepted
accepted_with_warnings
rejected
skipped_duplicate
quarantined
fatal_batch
```

Upload-mode strings accepted by F# boundary normalization:

```text
normal
retry
conservativerecovery
aggressiverecovery
```

The input can include underscores because normalization removes `_`, so `conservative_recovery` maps the same way.

## 6. The Intended Audience And Message

Audience: still junior C#/.NET developers, but v3 makes the book more useful for intermediate devs and tech leads too. The focus shifts from "look at fancy languages" to "look at how types protect a business workflow as it grows edges."

The closer message shifted slightly.

V2 closer:

> Each rung makes a class of error unrepresentable rather than unlikely.

V3 closer:

> Types help most when they protect the boundary between messy reality and the domain. The domain can be clean, but CSV, repositories, audit, and reports are where slop tries to sneak back in.

Best concise takeaway:

F# is the best teaching bridge for this team: close enough to .NET to feel usable, typed enough to show the blub-paradox punchline, and practical enough to model real boundaries without turning the exercise into Haskell-only theory.

## 7. What Is Still In Flight

Nothing in v3 is still in progress in the v3 repo. The v3 branch was clean and complete when inspected.

Final validation from `docs/v3-results.md`:

```text
dotnet test csharp-fuel-engine/FuelUploadEngine.slnx: passed, 54 tests
dotnet test fsharp-fuel-engine/FSharpFuelEngine.slnx: passed, 42 tests
cabal test all: passed, 50 examples + 4 QuickCheck properties
cargo fmt --check && cargo test && cargo clippy --all-targets -- -D warnings: passed after cargo fmt
```

There is no v4 experiment currently planned that should affect the v3 book chapter. The recommendation after v3 was not "run v4"; it was "turn the F# result into a team learning artifact."

That artifact now exists:

```text
docs/fsharp-learning-guide.md
```

For the Quarto book, I would treat v3 as complete and fold it into the story as the final lesson: types are not just for the pure core; they are also how you keep integration pressure from undoing the domain model.
