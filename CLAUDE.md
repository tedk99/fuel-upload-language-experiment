# Fuel Upload Language Experiment — Agent Guide

This repository compares how a pure decision-engine domain problem is modelled across multiple programming languages. Each implementation is isolated in its own folder with no shared runtime, no database, and no application integration. Each project is a self-contained library.

## Quick orientation

**Active projects** (V2 evolved — 5 phases of change):

| Folder | Language | Build |
|---|---|---|
| `csharp-fuel-engine` | C# / .NET 10.0 | `dotnet test csharp-fuel-engine/FuelUploadEngine.slnx` |
| `fsharp-fuel-engine` | F# / .NET 10.0 | `dotnet test fsharp-fuel-engine/FSharpFuelEngine.slnx` |
| `haskell-fuel-engine` | Haskell GHC 9.6.7 | `cd haskell-fuel-engine && cabal test all` |
| `rust-fuel-engine` | Rust 1.95.0 | `cd rust-fuel-engine && cargo fmt --check && cargo test && cargo clippy --all-targets -- -D warnings` |

**Archived projects** (V1 baseline only — frozen at V2 phase 0):
`archived/typescript-fuel-engine`, `archived/purescript-fuel-engine` — see `archived/README.md`.

**Reference implementation**: Haskell — strongest algebraic model, only property tests, best correctness oracle.  
**Best .NET domain model**: F# — discriminated unions, non-empty wrappers, compact.  
**Most practical for C#/.NET production**: C# — familiar, broad test coverage, most realistic DTO boundary.

**Experiment docs** (human-oriented): `docs/v2-results.md` (final scores), `docs/v2-experiment-plan.md`, `docs/agent-notes/` (phase-by-phase decisions).

Each active project folder has an `AGENTS.md` with its module map, key types, public API, and known limitations.

---

## The domain problem

A fuel upload batch is a list of fuel rows from a fleet management system. Each row is classified into exactly one outcome. The classifier is a pure function — no I/O, no mutation, no side effects.

Inputs per row:
- `ParsedFuelRow` — row data (vehicle identifier, quantity, cost, date, merchant, etc.)
- `VehicleLookupResult` — outcome of resolving the vehicle identifier (found, not found, ambiguous, service unavailable)
- `DuplicateCheckResult` — whether this row is a duplicate and the state of the previous attempt
- `ValidationConfig` — numeric thresholds (max quantity, max cost, warning thresholds, suspicious-pattern thresholds)
- `UploadMode` — determines how duplicates are handled

---

## Upload modes

| Mode | What it means for duplicates |
|---|---|
| `Normal` | Duplicates are always skipped |
| `Retry` | Duplicates are skipped unless the previous attempt is explicitly retryable |
| `ConservativeRecovery` | Duplicate accepted only if previous attempt failed before canonical finalization |
| `AggressiveRecovery` | More permissive recovery; accepted if previous attempt was not finalized (see DuplicatePolicy) |

---

## Row decision outcomes

Every row produces exactly one of these six outcomes:

| Outcome | Condition |
|---|---|
| `Accepted` | Valid row, vehicle found, no duplicate conflict; clean transaction |
| `AcceptedWithWarnings` | Accepted but one or more threshold warnings fired (high quantity, high cost, etc.) |
| `Quarantined` | Structurally valid but suspicious patterns detected; flagged for manual review |
| `SkippedDuplicate` | Row is a duplicate; mode/state rules say to skip it |
| `Rejected` | Validation errors, vehicle not found or ambiguous, or unacceptable duplicate |
| `Fatal` | Lookup service unavailable; entire batch is blocked (not just this row) |

---

## Shared domain rules

All implementations must satisfy these rules:

1. No accepted transaction may carry validation errors.
2. A duplicate in Normal mode is always skipped.
3. A duplicate in Retry mode is skipped unless the previous attempt is explicitly retryable.
4. A duplicate in Recovery mode may be accepted only if the previous attempt failed before canonical finalization.
5. Every rejection must carry a typed rejection reason (no raw strings).
6. Warnings do not block upload.
7. Fatal errors block the entire batch.
8. Batch summary must be derived from per-row decisions — never separately mutated.
9. No raw string statuses for domain decisions.
10. No nullable/null/Maybe-heavy modelling where a sum type would be clearer.
11. No boolean soup.
12. Use enums, records, discriminated unions, sealed hierarchies, or algebraic data types depending on the language.
13. Keep the main row decision function pure.
14. Keep side effects out of the implementation.

---

## Key type vocabulary (cross-language)

The same concepts appear in every implementation under slightly different names:

| Concept | C# | F# | Haskell | Rust |
|---|---|---|---|---|
| Row input | `FuelRow` | `ParsedFuelRow` | `ParsedFuelRow` | `ParsedFuelRow` |
| Row + lookup context | (fields on request) | `FuelRowContext` | `RowContext` | `RowInput` |
| Row outcome | `RowDecision` | `RowDecision` | `RowDecision` | `RowDecision` |
| Batch outcome | `BatchDecision` | `BatchDecision` | `BatchDecision` | `BatchDecision` |
| Upload mode | `UploadMode` (enum) | `UploadMode` (DU) | `UploadMode` (ADT) | `UploadMode` (enum) |
| Classify single row | `ClassifyRow(...)` | `classifyRow ...` | `classifyRow ...` | `classify_row(...)` |
| Classify batch | `ClassifyBatch(...)` | `classifyBatch ...` | `classifyBatch ...` | `classify_batch(...)` |
| DTO entry point | `FuelUploadApplicationService.Classify` | `FuelUploadFacade.Classify` | `classifyUploadDto` | `FuelUploadApplicationService::classify` |

---

## Where to look for each concern

| Concern | Location |
|---|---|
| Canonical domain type definitions | `haskell-fuel-engine/src/FuelUpload/Domain/Decision.hs` |
| Most idiomatic .NET type model | `fsharp-fuel-engine/FuelUpload.Domain/Decision.fs` |
| Duplicate policy / recovery matrix | `*/DuplicatePolicy.*` in each project |
| Quarantine detection logic | `csharp-fuel-engine/src/FuelUploadEngine/Engine/QuarantinePolicy.cs` |
| Application boundary (most complete) | `csharp-fuel-engine/src/FuelUploadEngine/Application/` |
| Property tests (only Haskell) | `haskell-fuel-engine/test/FuelUpload/Properties.hs` |
| Final scoring across languages | `docs/v2-results.md` |
| Phase-by-phase decisions | `docs/agent-notes/` |
| Exact subagent prompts used | `prompts/v2/` |

---

## V2 evolution phases

| Phase | What changed |
|---|---|
| 0 | Docs and plan only — no code changes |
| 1 | Idiomatic project structure refactoring (splitting monoliths into modules) |
| 2 | Added `Quarantined` outcome with typed `QuarantineReason` values |
| 3 | Split `Recovery` mode into `ConservativeRecovery` and `AggressiveRecovery` |
| 4 | Added application boundary: DTO → domain → DTO mapping layer |
| 5 | Cross-language scoring report (`docs/v2-results.md`) |

---

## V2 scores (summary)

| Rank | Language | Score /100 | Verdict |
|---|---|---|---|
| 1 | Haskell | 90 | Best reference model; least practical for .NET |
| 2 | F# | 89 | Best .NET domain model; rougher boundary |
| 3 | C# | 88 | Most practical production fit |
| 4 | Rust | 87 | Strong compiler feedback; not natural for .NET context |

---

## Format checks

```
dotnet format csharp-fuel-engine/FuelUploadEngine.slnx --verify-no-changes --verbosity minimal
dotnet format fsharp-fuel-engine/FSharpFuelEngine.slnx --verify-no-changes --verbosity minimal
cd rust-fuel-engine && cargo fmt --check
```

Haskell: no formatter configured; GHC warnings enabled via `cabal.project` or `.cabal` flags.
