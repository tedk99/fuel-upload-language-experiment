# Fuel Upload Language Experiment

This repository compares how well different languages help an LLM produce correct, low-slop domain code for the same small pure decision engine.

The domain is a fuel upload row classifier. Each implementation is isolated in its own folder and does not integrate with any application, database, CSV parser, UI, or FleetSoft code.

## V2 Idiomatic Evolution Experiment

V2 keeps the V1 implementations as the baseline, then tests how safely the strongest target languages evolve under change. Phase 0 scaffolding is captured in the [V2 experiment plan](docs/v2-experiment-plan.md), [V2 scoring rubric](docs/v2-scoring-rubric.md), and [V2 agent log](docs/v2-agent-log.md). Final target-language scoring is in the [V2 cross-language results](docs/v2-results.md), and the exact subagent prompts are captured in the [V2 prompt logs](prompts/v2/README.md).

## Original Goal

Implement the same pure decision engine across multiple languages:

- C# / .NET
- F# on .NET
- Haskell
- TypeScript with strict mode
- Rust
- PureScript

Each implementation models:

- parsed fuel rows
- transactions
- vehicle lookup results
- duplicate states
- validation errors
- warnings
- rejection reasons
- fatal errors
- row decisions
- batch decisions
- batch summaries

The shared rules:

1. No accepted transaction may have validation errors.
2. A duplicate in Normal mode is skipped.
3. A duplicate in Retry mode is skipped unless the previous attempt is explicitly retryable.
4. A duplicate in Recovery mode may be accepted only if the previous attempt failed before canonical finalization.
5. Every rejection must have a typed rejection reason.
6. Warnings do not block upload.
7. Fatal errors block the entire batch.
8. The batch summary must be derived from per-row decisions, not separately mutated.
9. Avoid raw string statuses for domain decisions.
10. Avoid nullable/null/Maybe-heavy modelling where a sum type or explicit domain type would be better.
11. Avoid boolean soup.
12. Prefer enums, records, discriminated unions, sealed hierarchies, or algebraic data types depending on language.
13. Keep the main row decision function pure.
14. Keep side effects out of the implementation.

## Projects

| Language | Folder | Main Verification Command |
|---|---|---|
| C# | `csharp-fuel-engine` | `dotnet test` |
| F# | `fsharp-fuel-engine` | `dotnet test` |
| Haskell | `haskell-fuel-engine` | `cabal test all` |
| Rust | `rust-fuel-engine` | `cargo fmt --check && cargo test && cargo clippy --all-targets -- -D warnings` |
| TypeScript | `typescript-fuel-engine` | `npm run typecheck && npm test && npm run build` |
| PureScript | `purescript-fuel-engine` | `spago test` |

## Verified Results

All six implementations built and tested successfully in the local environment.

| Language | Build/Test Status | Type Safety | Invalid-State Prevention | Raw Status Strings | Nullable/Maybe/Option Domain Fields | Boolean Domain Flags | Tests | Property Tests | Score |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|
| C# | Passed | 4 | 4 | 0 | 0 | 1 computed property | 18 | 0 | 88 |
| F# | Passed | 5 | 4 | 0 | 0 | 2 config flags | 10 | 0 | 88 |
| Haskell | Passed | 5 | 5 | 0 | 0 | 0 | 17 | 4 | 88 |
| Rust | Passed | 5 | 4 | 0 | 0 | 0 | 12 | 0 | 86 |
| TypeScript | Passed | 4 | 3 | 0 except discriminant tags | 0 | 0 | 9 | 0 | 88 |
| PureScript | Passed | 5 | 4 | 0 | 0 | 0 | 10 | 0 | 86 |

## Recommendations

Best overall correctness language: Haskell.

Haskell produced the strongest proof-like shape: algebraic data types, `NonEmpty`, pure functions, and property tests.

Best .NET compromise: F#.

F# keeps most of the algebraic modelling benefits while staying close to a .NET application boundary. The main weakness is C# interop ergonomics around discriminated unions.

Best practical app-integration choice: C# or F#.

For a C# production codebase, C# is the easiest drop-in. For a correctness-oriented domain core inside a .NET system, F# is the stronger model.

Most awkward tooling choice: PureScript.

PureScript worked, but Spago registry setup and dependency fetching were more fragile than the other toolchains. The local shell now sets `NODE_OPTIONS=--dns-result-order=ipv4first` because Spago initially hung on IPv6 dependency fetches.

Rust result:

Rust was a strong comparison point. It is not functional in the Haskell/F#/PureScript sense, but enums, exhaustive `match`, no nulls, explicit `Result`-style modelling, and strict compiler feedback made it good at reducing sloppy domain states. In this pure decision-engine problem, ownership/lifetime complexity stayed low.

## Follow-On Candidates

The next comparison languages are:

- Scala 3
- OCaml
- ReScript

These should be added as separate isolated folders, with the same rules and metrics.

## Tooling Notes

Verified local tools:

- .NET SDK `10.0.107`
- Node `22.22.2`
- npm `10.9.7`
- TypeScript `6.0.3`
- PureScript `0.15.16`
- Spago `1.0.4`
- GHC `9.6.7`
- cabal `3.14.2.0`
- stack `3.7.1`
- Haskell Language Server `2.13.0.0`
- Rust `1.95.0`
- cargo `1.95.0`
