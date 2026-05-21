# V3 Integration Pressure Experiment Plan

## What V2 Showed

V2 showed that C#, F#, Haskell, and Rust can all evolve the original pure decision engine without losing the core rules when the changes stay mostly inside the domain and application boundary. The strongest results kept row decisions typed, preserved fatal and duplicate behavior, avoided raw status strings in domain code, and made summaries derive from decisions instead of drift-prone counters.

V2 also showed the next limit of the experiment. The code became more idiomatic and safer under incremental change, but most pressure still came from controlled DTO mapping. It did not yet force the implementations to handle messier import shapes, event-style projections, persistence ports, or operational reporting in one coherent integration flow.

## Why V3 Adds Integration Pressure

V3 keeps the domain rules central, but moves the experiment closer to production seams where slop usually enters. CSV-shaped data brings parsing and missing-field pressure. Audit and event projections test whether every decision remains explainable without duplicating rule logic. Repository ports test whether persistence concerns can stay outside the domain model. Operational reports test whether teams can derive useful production output without mutable counters or parallel decision logic.

The goal is not to build full applications. The goal is to see which languages and project shapes keep integration code explicit, typed, testable, and separated from the pure decision engine when external shapes become harder to ignore.

## Target Languages

Active V3 targets:

- C#
- F#
- Haskell
- Rust

Frozen baseline languages:

- TypeScript
- PureScript

TypeScript and PureScript remain useful as baseline comparison points from earlier work, but V3 implementation phases should prioritize the active target languages unless a later phase explicitly reopens them.

## Phases

The five V3 phases are:

1. CSV-shaped import boundary
2. Audit/event projection
3. Persistence-shaped repository port
4. Operational batch report
5. v3 scoring report

### Phase 1: CSV-shaped import boundary

Add an import boundary that accepts CSV-shaped rows and converts them into domain inputs. CSV parsing, raw strings, missing fields, and edge validation should stay outside the domain core.

### Phase 2: Audit/event projection

Project row and batch decisions into audit/event-shaped outputs. The projection should be derived from typed decisions and reasons, not from duplicated decision rules or raw status reconstruction.

### Phase 3: Persistence-shaped repository port

Introduce a repository-shaped port for lookup and persistence interactions. The domain should depend on typed capabilities or ports, not database rows, ORM details, SQL concepts, or storage-specific status strings.

### Phase 4: Operational batch report

Produce an operational batch report from decisions, audit data, and summary information. Counts and sections should be derived from canonical decision data instead of mutable counters that can drift.

### Phase 5: v3 scoring report

Score the active target languages using the V3 rubric. The report should compare build/test reliability, boundary integrity, typed integration errors, rule preservation, change safety, idiomatic integration shape, and practical production fit.
