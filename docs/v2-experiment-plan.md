# V2 Idiomatic Evolution Experiment Plan

## Why V1 Was Useful

V1 was useful because it gave every language the same small, pure decision-engine kata. That made the first comparison easy to run and easy to score: each implementation could be built, tested, and inspected without application infrastructure getting in the way.

That shape also limited what the experiment could tell us. The problem was kata-shaped: isolated, greenfield, and mostly static. It measured whether an agent could produce a good first version of a domain core, but it did not test how safely that code changes when requirements evolve, integration pressure appears, or a broken outcome must be quarantined and recovered.

## V2 Focus

V2 focuses on evolution and change safety. The goal is to compare how well each language and project shape supports incremental changes without letting slop accumulate in the domain model, tests, and application boundary.

The experiment should emphasize:

- how idiomatic the project structure becomes when the code grows beyond one file;
- whether bad outcomes can be represented explicitly and quarantined;
- whether recovery behavior can evolve without weakening existing rules;
- whether DTO/application mapping keeps domain decisions pure and typed;
- whether the final scoring can compare languages on practical change safety, not only initial correctness.

## Target Languages

Active V2 targets:

- C#
- F#
- Haskell
- Rust

Frozen baseline targets, if present:

- TypeScript
- PureScript

TypeScript and PureScript remain useful as V1 comparison baselines, but V2 implementation work should prioritize the active targets unless a later phase explicitly reopens them.

## Phases

### Phase 1: Idiomatic Project Structure

Reshape each active target into a more idiomatic project layout for its ecosystem. The output should keep behavior equivalent while making later changes easier to isolate, test, and review.

### Phase 2: Quarantined Outcome

Introduce an explicit quarantined outcome for rows that should not be accepted, skipped, rejected, or treated as fatal. The model should make quarantine reasons typed and visible in summaries.

### Phase 3: Recovery Mode Evolution

Evolve recovery behavior while preserving the existing duplicate and retry rules. The implementation should show whether each language makes the changed decision matrix exhaustive, local, and testable.

### Phase 4: Application Boundary / DTO Mapping

Add an application-facing boundary that maps external DTO-shaped data into the domain model and maps domain decisions back out. Parsing, nullability, and raw strings should be contained at the edge.

### Phase 5: Cross-Language Scoring Report

Produce a final scoring report across languages using the V2 rubric. The report should account for build/test reliability, model quality, invalid-state prevention, rule correctness, change safety, idiomatic shape, and practical integration fit.
