# Phase 1 Prompt Log

- Phase: Idiomatic project structure
- Date: 2026-05-21
- Branch: `experiment/v2-idiomatic-evolution`
- Agents: Initial Phase 1 subagent, replacement Phase 1 subagent

The initial Phase 1 worker disconnected after leaving a partial restructure in the worktree. A replacement worker was then prompted to inspect and complete the in-progress Phase 1 changes.

## Exact Initial Subagent Prompt

````text
You are Phase 1 subagent for repo /home/ted/Develop/experiment on branch experiment/v2-idiomatic-evolution. You are not alone in the codebase; do not revert edits made by others. Work only on PHASE 1: Idiomatic project structure. Make the smallest coherent change that restructures while preserving v1 behavior.

Scope ownership:
- You may edit csharp-fuel-engine, fsharp-fuel-engine, haskell-fuel-engine, rust-fuel-engine.
- You may edit docs/v2-agent-log.md and create docs/agent-notes/phase-1.md.
- Avoid TypeScript and PureScript except no changes should be needed.

Task:
Restructure C#, F#, Haskell, and Rust into more realistic idiomatic project layouts while preserving existing behavior. Do not add new domain behavior.

Target shapes:
C#:
csharp-fuel-engine/src/FuelUploadEngine/Domain/{Primitives.cs,FuelRow.cs,Vehicle.cs,VehicleLookupResult.cs,DuplicateCheckResult.cs,PreviousUploadOutcome.cs,Validation.cs,RowDecision.cs,BatchDecision.cs}
csharp-fuel-engine/src/FuelUploadEngine/Engine/{FuelRowValidator.cs,WarningPolicy.cs,DuplicatePolicy.cs,TransactionFactory.cs,FuelUploadDecisionEngine.cs,BatchSummaryCalculator.cs}
csharp-fuel-engine/tests/FuelUploadEngine.Tests/
Use idiomatic modern C#, nullable enabled, sealed records/sealed hierarchies where useful, no unjustified deps.

F#:
fsharp-fuel-engine/FuelUpload.Domain/{Primitives.fs,FuelRow.fs,Vehicle.fs,Duplicate.fs,Validation.fs,Decision.fs,BatchSummary.fs,DecisionEngine.fs}
fsharp-fuel-engine/FuelUpload.Domain.Tests/
Use idiomatic F#, keep core idiomatic, no C# facade yet.

Haskell:
haskell-fuel-engine/src/FuelUpload/Domain/Primitive.hs
haskell-fuel-engine/src/FuelUpload/Domain/Row.hs
haskell-fuel-engine/src/FuelUpload/Domain/Vehicle.hs
haskell-fuel-engine/src/FuelUpload/Domain/Duplicate.hs
haskell-fuel-engine/src/FuelUpload/Domain/Decision.hs
haskell-fuel-engine/src/FuelUpload/Validation.hs
haskell-fuel-engine/src/FuelUpload/DuplicatePolicy.hs
haskell-fuel-engine/src/FuelUpload/DecisionEngine.hs
haskell-fuel-engine/src/FuelUpload/Summary.hs
haskell-fuel-engine/test/FuelUpload/DecisionEngineSpec.hs
haskell-fuel-engine/test/FuelUpload/Properties.hs
Prefer newtypes and ADTs; use NonEmpty where reasonable; avoid unnecessary advanced type tricks.

Rust:
rust-fuel-engine/src/lib.rs
rust-fuel-engine/src/domain/{mod.rs,primitives.rs,row.rs,vehicle.rs,duplicate.rs,decision.rs,validation.rs}
rust-fuel-engine/src/engine/{mod.rs,classifier.rs,duplicate_policy.rs,summary.rs,transaction_factory.rs}
rust-fuel-engine/tests/{decision_engine_tests.rs,duplicate_policy_tests.rs}
Use idiomatic modules, enums and exhaustive match. Avoid f64 for money if practical without derailing phase.

Tests:
Run builds/tests for all four target languages if possible. If a toolchain is missing/fails, record clearly in docs/v2-agent-log.md and docs/agent-notes/phase-1.md.

Update docs/v2-agent-log.md with your phase row. Commit exactly your coherent Phase 1 work with message: refactor: split target languages into idiomatic project structures

Final response: include commit SHA, tests/builds run and results, files changed summary, and any toolchain failures.
````

## Exact Replacement Subagent Prompt

````text
You are the replacement Phase 1 subagent for repo /home/ted/Develop/experiment on branch experiment/v2-idiomatic-evolution. The previous Phase 1 worker disconnected and left a partial restructure in the shared worktree. You are not alone in the codebase; do not revert edits made by others. Work only on PHASE 1: Idiomatic project structure. Preserve and complete the current in-progress Phase 1 changes rather than starting unrelated work.

First inspect `git status --short` and the target language trees. Complete the smallest coherent restructure that preserves v1 behavior.

Scope ownership:
- You may edit csharp-fuel-engine, fsharp-fuel-engine, haskell-fuel-engine, rust-fuel-engine.
- You may edit docs/v2-agent-log.md and create docs/agent-notes/phase-1.md.
- Do not edit TypeScript or PureScript.

Task:
Restructure C#, F#, Haskell, and Rust into more realistic idiomatic project layouts while preserving existing behavior. Do not add new domain behavior.

Target shapes:
C#:
csharp-fuel-engine/src/FuelUploadEngine/Domain/{Primitives.cs,FuelRow.cs,Vehicle.cs,VehicleLookupResult.cs,DuplicateCheckResult.cs,PreviousUploadOutcome.cs,Validation.cs,RowDecision.cs,BatchDecision.cs}
csharp-fuel-engine/src/FuelUploadEngine/Engine/{FuelRowValidator.cs,WarningPolicy.cs,DuplicatePolicy.cs,TransactionFactory.cs,FuelUploadDecisionEngine.cs,BatchSummaryCalculator.cs}
csharp-fuel-engine/tests/FuelUploadEngine.Tests/
Use idiomatic modern C#, nullable enabled, sealed records/sealed hierarchies where useful, no unjustified deps.

F#:
fsharp-fuel-engine/FuelUpload.Domain/{Primitives.fs,FuelRow.fs,Vehicle.fs,Duplicate.fs,Validation.fs,Decision.fs,BatchSummary.fs,DecisionEngine.fs}
fsharp-fuel-engine/FuelUpload.Domain.Tests/
Use idiomatic F#, keep core idiomatic, no C# facade yet.

Haskell:
haskell-fuel-engine/src/FuelUpload/Domain/Primitive.hs
haskell-fuel-engine/src/FuelUpload/Domain/Row.hs
haskell-fuel-engine/src/FuelUpload/Domain/Vehicle.hs
haskell-fuel-engine/src/FuelUpload/Domain/Duplicate.hs
haskell-fuel-engine/src/FuelUpload/Domain/Decision.hs
haskell-fuel-engine/src/FuelUpload/Validation.hs
haskell-fuel-engine/src/FuelUpload/DuplicatePolicy.hs
haskell-fuel-engine/src/FuelUpload/DecisionEngine.hs
haskell-fuel-engine/src/FuelUpload/Summary.hs
haskell-fuel-engine/test/FuelUpload/DecisionEngineSpec.hs
haskell-fuel-engine/test/FuelUpload/Properties.hs
Prefer newtypes and ADTs; use NonEmpty where reasonable; avoid unnecessary advanced type tricks.

Rust:
rust-fuel-engine/src/lib.rs
rust-fuel-engine/src/domain/{mod.rs,primitives.rs,row.rs,vehicle.rs,duplicate.rs,decision.rs,validation.rs}
rust-fuel-engine/src/engine/{mod.rs,classifier.rs,duplicate_policy.rs,summary.rs,transaction_factory.rs}
rust-fuel-engine/tests/{decision_engine_tests.rs,duplicate_policy_tests.rs}
Use idiomatic modules, enums and exhaustive match. Avoid f64 for money if practical without derailing phase.

Tests:
Run builds/tests for all four target languages if possible. If a toolchain is missing/fails, record clearly in docs/v2-agent-log.md and docs/agent-notes/phase-1.md.

Update docs/v2-agent-log.md with your phase row. Commit exactly your coherent Phase 1 work with message: refactor: split target languages into idiomatic project structures

Final response: include commit SHA, tests/builds run and results, files changed summary, and any toolchain failures.
````

