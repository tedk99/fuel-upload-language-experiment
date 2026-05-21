# Fuel Upload Language Experiment

This repository compares how well different languages help an LLM produce correct, low-slop domain code for the same small pure decision engine.

The domain is a fuel upload row classifier. Each implementation is isolated in its own folder and does not integrate with any application, database, CSV parser, UI, or FleetSoft code.

## The Book — *Blub & Fuel: Five Engines, One Domain*

A 20-chapter teaching book walks junior C# developers up the language ladder using these engines as the worked example.

There are two checked-in ways to read it without installing Quarto:

- **Static reader** — `docs/book/index.html`. Hand-rolled HTML, mermaid as source. Fine for skim/search.
- **JupyterLab notebooks** — `docs/book-notebooks/index.ipynb`. Each chapter is split into prose markdown cells and language-tagged code cells, with mermaid diagrams **pre-rendered to inline SVG**. Open in JupyterLab for cell-by-cell navigation.

Source lives under [`book/`](book/). To refresh after editing the book:

```bash
python3 tools/render_static_book.py        # static HTML reader
python3 tools/build_book_notebooks.py      # notebooks (requires Quarto for SVG rendering)
```

To serve the notebook view locally:

```bash
bash tools/serve_quarto_book_jupyter.sh    # rebuilds notebooks, starts JupyterLab on :8888
```

To publish the Jupyter container and a public static copy of the book on the
Ubuntu/Docker host:

```bash
./okgo deploy
```

That deploys the repo contents into the JupyterLab container, so any checked-in
notebooks and source files are available there, and also starts a public static
reader on port `8898`. Jupyter is bound to localhost on the server at port
`8899`; use an SSH tunnel when you need it.

If you do have Quarto installed, you can still use the richer Quarto preview/render flow:

```bash
# Read locally with live reload
cd book
quarto preview                   # opens http://localhost:port/ with sidebar + search

# Or render once to static files
quarto render                    # -> book/_build/index.html (HTML site)
quarto render --to epub          # -> book/_build/Blub---Fuel--Five-Engines,-One-Domain.epub
```

Quarto-only prerequisites:

- [Quarto](https://quarto.org) 1.5 or newer.
- For EPUB output with rendered diagrams: `quarto install chrome-headless-shell` (one-time; lets Quarto pre-render the mermaid diagrams to SVG so they appear as images in the EPUB rather than as source code).

The book is structured in three parts:

- **Part I — The Journey** (chapters 01–10): the V2 climb, from normal junior C# through idiomatic C#, F#, Haskell, and Rust.
- **Part Ib — The Boundary Returns** (chapters 11–13): the V3 lesson — the bugs come back at integration edges (CSV, repositories, audit, reports) and how each language handles them.
- **Part II — The Reference** (chapters r1–r7): topic-indexed side-by-side lookups (decision, validation, recovery, boundary, exhaustiveness, mutability, null).

## V2 Idiomatic Evolution Experiment

V2 keeps the V1 implementations as the baseline, then tests how safely the strongest target languages evolve under change. Phase 0 scaffolding is captured in the [V2 experiment plan](docs/v2-experiment-plan.md), [V2 scoring rubric](docs/v2-scoring-rubric.md), and [V2 agent log](docs/v2-agent-log.md). Final target-language scoring is in the [V2 cross-language results](docs/v2-results.md), and the exact subagent prompts are captured in the [V2 prompt logs](prompts/v2/README.md).

## V3 Integration Pressure Experiment

V3 builds on V2 by testing how the strongest target languages hold their domain boundaries under integration-shaped pressure. Phase 0 scaffolding is captured in the [V3 experiment plan](docs/v3-experiment-plan.md), [V3 scoring rubric](docs/v3-scoring-rubric.md), and [V3 agent log](docs/v3-agent-log.md). Final target-language scoring is in the [V3 cross-language results](docs/v3-results.md). The team-facing F# follow-up is the [F# fuel upload learning guide](docs/fsharp-learning-guide.md).

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

Active V2 targets:

| Language | Folder | Main Verification Command |
|---|---|---|
| C# | `csharp-fuel-engine` | `dotnet test` |
| F# | `fsharp-fuel-engine` | `dotnet test` |
| Haskell | `haskell-fuel-engine` | `cabal test all` |
| Rust | `rust-fuel-engine` | `cargo fmt --check && cargo test && cargo clippy --all-targets -- -D warnings` |

V1 baseline (frozen, not evolved in V2):

| Language | Folder | Main Verification Command |
|---|---|---|
| TypeScript | `archived/typescript-fuel-engine` | `npm run typecheck && npm test && npm run build` |
| PureScript | `archived/purescript-fuel-engine` | `spago test` |

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
