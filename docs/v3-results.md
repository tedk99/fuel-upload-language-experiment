# V3 Cross-Language Scoring Report

Phase 5 scores only the active V3 target languages: C#, F#, Haskell, and Rust. Scores use the [V3 scoring rubric](v3-scoring-rubric.md) and are based on source inspection plus local validation run on 2026-05-21.

| Rank | Language | Overall /100 | Build/test /15 | Boundary integrity /20 | Typed errors /15 | Rule preservation /20 | Change safety /15 | Idiomatic integration /10 | Practical fit /5 | Verdict |
|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---|
| 1 | F# | 91 | 15 | 18 | 14 | 19 | 14 | 7 | 4 | Best core-plus-boundary compromise for a .NET shop if F# is acceptable. |
| 2 | C# | 90 | 15 | 17 | 13 | 19 | 12 | 9 | 5 | Most practical production shape, with weaker compiler pressure at decision boundaries. |
| 3 | Haskell | 89 | 14 | 19 | 13 | 18 | 15 | 8 | 2 | Best reference model and test story, less complete and practical as an app boundary. |
| 4 | Rust | 88 | 14 | 18 | 13 | 19 | 13 | 8 | 3 | Strong tooling and enum discipline, but less natural for the target .NET environment. |

## Validation Run

- `dotnet test csharp-fuel-engine/FuelUploadEngine.slnx`: passed, 54 tests.
- `dotnet test fsharp-fuel-engine/FSharpFuelEngine.slnx`: passed, 42 tests.
- `cabal test all` in `haskell-fuel-engine`: passed, 50 examples and 4 QuickCheck properties.
- `cargo fmt --check && cargo test && cargo clippy --all-targets -- -D warnings` in `rust-fuel-engine`: initially failed formatting only; after `cargo fmt`, passed with 45 tests and clippy clean.

## C#

- Overall verdict: C# is the easiest V3 result to ship inside a C#/.NET-heavy business app. It has the most recognizable service, DTO, repository, and test shape, but it uses runtime checks and tests where F#, Haskell, and Rust get stronger exhaustive handling.
- Strongest parts: clear application services, typed import and mapping error records, repository interfaces that stay outside the domain, decimal money and fuel values, broad xUnit coverage, and operational reports derived from `BatchDecision`.
- Weakest parts: repository lookup results are translated back into DTO status strings before being mapped into domain types, and sealed record switches still need default throw branches instead of compiler-enforced exhaustiveness.
- Specific correctness risks: adding a new decision or repository result can miss a switch until tests hit it; repository failures are represented as fatal row decisions rather than an application-level failure result; imported dates are shape-validated before later mapping.
- Specific slop indicators: raw string statuses remain prominent in DTOs and repository adapter glue; import and mapping code repeat parsing branches; several future-case guards are runtime exceptions.
- What stayed clean at the domain boundary: CSV-shaped strings, repository ports, audit DTO status text, and operational reporting stayed outside the pure classifier and summary rules.
- What leaked or became awkward: repository-backed classification mutates a DTO-shaped row with status strings, then reuses the normal mapper. That is practical but less clean than mapping repository results directly to row context.
- How easy CSV-shaped import was: easy and readable. The import mapper returns typed `FuelImportError` values and then reuses the application service.
- How easy audit projection was: easy. It projects from `RowDecision` and keeps typed audit kinds internally, with strings only in DTO output.
- How realistic the repository port felt: most realistic for a C# app. The interfaces and fake tests look like ordinary application-layer code, despite the DTO rehydration shortcut.
- How safe the operational report was: safe enough for production review. Counts come from the domain summary, fatal batches suppress uploaded transactions, and row lists derive from decisions.
- Production suitability for a C#/.NET-heavy business app: highest. It fits staffing, debugging, deployment, and review habits best.
- Reference implementation suitability: good but not best. It is a practical reference for app shape, not the strongest invariant model.
- Score rationale: full build/test and practical-fit credit; high rule preservation from 54 tests; deductions for weak exhaustiveness and status-string glue in repository and DTO boundaries.

## F#

- Overall verdict: F# is the best v3 compromise: a strong typed domain with a direct-enough integration boundary inside the .NET ecosystem.
- Strongest parts: discriminated unions for decisions, lookup results, duplicate state, repository errors, import errors, and operational status; direct repository-to-domain request mapping; compact report and audit projections from decisions.
- Weakest parts: the interop module is large and mixes several adapter concerns; `[<CLIMutable>]` records and string DTO fields weaken the otherwise strong model for C# callers.
- Specific correctness risks: C# callers can still construct null or default-ish interop records; DTO response text uses `%A` formatting; import date parsing accepts broader .NET parseable dates than the stricter C#/Rust `yyyy-MM-dd` checks.
- Specific slop indicators: broad interop module, CLIMutable boundary records, empty strings in DTO response fields, and prototype-like text rendering for domain values.
- What stayed clean at the domain boundary: CSV cells, repository calls, audit records, operational reports, and DTO text stayed outside `DecisionEngine`.
- What leaked or became awkward: C# interop needs mutable string-heavy records, and the facade shape is less polished than the C# application-service version.
- How easy CSV-shaped import was: straightforward. The mapper accumulates typed `FuelImportError` values and hands valid rows to the existing DTO boundary.
- How easy audit projection was: natural. Pattern matching over `RowDecision` keeps warning, quarantine, rejection, duplicate, and fatal cases explicit.
- How realistic the repository port felt: realistic for a mixed .NET solution. Repository calls map directly to typed `FuelRowContext` values rather than going through status strings.
- How safe the operational report was: strong. It uses existing summaries, suppresses upload IDs on fatal batches, and exhaustively handles row decisions in projection logic.
- Production suitability for a C#/.NET-heavy business app: strong if the team accepts an F# domain project. It is less friction than Haskell or Rust and safer than C# for domain evolution.
- Reference implementation suitability: strong. It is readable, compact, and close enough to .NET production code to be more than a pure reference.
- Score rationale: best combined boundary and change-safety score in the target environment; deductions for interop mutability, large adapter module, and less idiomatic service polish than C#.

## Haskell

- Overall verdict: Haskell remains the best reference implementation, especially for typed decisions and property-backed summaries, but V3 exposed its weaker production boundary fit for this organization shape.
- Strongest parts: algebraic domain model, `NonEmpty` warnings/errors/reasons where payloads are required, `Either`-based mapping and import errors, Hspec examples plus QuickCheck properties, and pure report/audit projections.
- Weakest parts: the API boundary is an in-process typed facade rather than a realistic service adapter; repository errors lose details when converted to fatal decisions; vehicle lookup lacks the ambiguous-vehicle case present in the other v3 implementations.
- Specific correctness risks: duplicate state still combines canonicalization and finalization independently; imported numeric parsing goes through `Double` before `Rational`; date validation is shape-only; missing ambiguous vehicle behavior narrows rule coverage.
- Specific slop indicators: DTO output uses `show`; strings remain common in edge DTOs; repository error detail is discarded; boundary ergonomics are less production-shaped.
- What stayed clean at the domain boundary: raw imports, repository ports, audit DTOs, and operational reports stayed outside the pure decision engine.
- What leaked or became awkward: no real serialization or .NET-friendly adapter shape emerged, and repository failures are reduced to row-number-only fatal errors.
- How easy CSV-shaped import was: type-safe but verbose. It accumulates typed import errors and reuses the DTO classifier, although empty import rows are treated as an error while normal DTO rows can still be empty.
- How easy audit projection was: very easy. Every event is a constructor-derived projection from `RowDecision`.
- How realistic the repository port felt: weaker than C#, F#, or Rust for production. The port is clear, but it feels like a modeling exercise rather than app infrastructure.
- How safe the operational report was: very safe. It derives from `BatchDecision`, uses existing summary data, and models fatal status explicitly.
- Production suitability for a C#/.NET-heavy business app: low. Tooling, hiring, deployment, and interop costs dominate despite the excellent core model.
- Reference implementation suitability: highest. It is the best correctness oracle, especially because of the property tests.
- Score rationale: top change-safety and boundary-cleanliness marks; deductions for practical fit, dropped repository error detail, incomplete vehicle lookup modeling, and less realistic app boundary.

## Rust

- Overall verdict: Rust is disciplined and well tooled. It keeps the domain mostly clean under integration pressure, but some public vector and `f64` choices leave invariants to tests and convention.
- Strongest parts: enums for decisions, lookup results, duplicate states, rules, and errors; exhaustive `match`; explicit repository traits; strong fmt/test/clippy feedback; good boundary and report tests.
- Weakest parts: the first verification run failed `cargo fmt --check`; monetary and quantity values use `f64`; some non-empty warning/error invariants are still plain `Vec`s; repository service rewrites DTO status strings before mapping.
- Specific correctness risks: public constructors can build empty warning or rejection vectors; `f64` can still carry rounding-sensitive values; synthetic `"missing-key-{row}"` transaction ids appear in one repository duplicate adapter path.
- Specific slop indicators: debug-formatted DTO strings, `Vec` payloads where non-empty wrappers would be stronger, and status-string rehydration in repository adapter code.
- What stayed clean at the domain boundary: CSV-shaped import parsing, repository traits, audit DTO text, and operational reporting stayed outside the classifier.
- What leaked or became awkward: the repository adapter converts typed repository results into string DTO fields, then parses them back into domain inputs. That mirrors C#'s shortcut and weakens the boundary.
- How easy CSV-shaped import was: easy and strongly typed. `TryFrom` implementations and typed import errors made the boundary explicit.
- How easy audit projection was: easy. Exhaustive matching keeps projection honest, though DTO output relies on debug strings.
- How realistic the repository port felt: technically solid as a Rust library, less natural for a .NET business application.
- How safe the operational report was: strong. It derives lists from `BatchDecision`, uses `Summary`, and handles fatal batches explicitly.
- Production suitability for a C#/.NET-heavy business app: moderate to low. It would be safe as a service or library but adds interop and staffing friction.
- Reference implementation suitability: good. It is a useful mechanically checked comparison, but Haskell and F# communicate the domain more directly.
- Score rationale: high rule preservation, boundary integrity, and tooling feedback after formatting; deductions for initial fmt drift, `f64`, public empty vectors, debug text, and lower .NET fit.

## Cross-language findings

1. Which language kept the cleanest domain boundary under integration pressure?
   F# kept the cleanest practical boundary. Haskell's pure domain stayed cleanest in isolation, but F# handled repository, import, audit, and report pressure while staying realistic for .NET.

2. Which language produced the most practical integration shape for a C#/.NET shop?
   C#. Its application services, DTOs, repositories, and xUnit tests match the expected shop shape.

3. Which language most reduced LLM slop at boundaries?
   Haskell reduced the most model slop through ADTs, `NonEmpty`, and property tests. F# was the best .NET-friendly version of that pressure.

4. Which language had the best typed error story?
   F#. It used typed `Result` errors across mapping, import, and repository paths while keeping enough detail visible to application code.

5. Which language had the best test story?
   Haskell. It had 50 examples plus 4 QuickCheck properties. C# had the largest conventional test count.

6. Which language had the worst tooling or build risk?
   Haskell has the highest organizational/toolchain risk for a C#/.NET shop. In this run, Rust had the only immediate verification issue: formatting drift that required `cargo fmt`.

7. Which implementation would you actually use for the fuel-upload core plus app boundary?
   F# if the organization will accept an F# domain project inside the .NET solution. Otherwise C# is the production-default choice.

8. Which implementation would you use as a reference only?
   Haskell. It is the best correctness reference and least practical production target for this environment.

9. Did v3 change the v2 conclusion?
   Yes, slightly. V2 favored Haskell as the best reference and C# as the practical app choice. V3 makes F# the strongest core-plus-boundary compromise because integration pressure rewards typed domain modeling and .NET proximity at the same time.

10. Which implementation would be easiest for a junior developer to safely extend?
    C# for a typical C#/.NET shop because the structure and tooling are familiar. For domain-rule changes specifically, F# is safer once the developer understands discriminated unions.
