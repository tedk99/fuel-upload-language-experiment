# V2 Cross-Language Scoring Report

Phase 5 scores only the active V2 target languages: C#, F#, Haskell, and Rust. Scores use the [V2 scoring rubric](v2-scoring-rubric.md) and are based on source inspection plus local validation run on 2026-05-21.

| Rank | Language | Overall /100 | Build/test /15 | Domain model /20 | Invalid-state prevention /15 | Rule correctness /20 | Change safety /15 | Idiomatic shape /10 | Practical fit /5 | Verdict |
|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---|
| 1 | Haskell | 90 | 14 | 19 | 14 | 18 | 14 | 9 | 2 | Best reference model and test story, least practical for a C#/.NET shop. |
| 2 | F# | 89 | 14 | 18 | 14 | 18 | 13 | 8 | 4 | Best .NET-oriented domain model, with a rougher DTO boundary. |
| 3 | C# | 88 | 15 | 16 | 12 | 19 | 12 | 9 | 5 | Most practical production fit for a C# business app, with weaker exhaustiveness. |
| 4 | Rust | 87 | 15 | 17 | 12 | 18 | 13 | 9 | 3 | Strong compiler feedback and tooling, but less natural for the target .NET context. |

## Validation Run

- `dotnet test csharp-fuel-engine/FuelUploadEngine.slnx`: passed, 32 tests.
- `dotnet format csharp-fuel-engine/FuelUploadEngine.slnx --verify-no-changes --verbosity minimal`: passed.
- `dotnet test fsharp-fuel-engine/FSharpFuelEngine.slnx`: passed, 20 tests.
- `dotnet format fsharp-fuel-engine/FSharpFuelEngine.slnx --verify-no-changes --verbosity minimal`: passed, with a workspace-load warning.
- `cabal test all` in `haskell-fuel-engine`: passed, 28 examples and 4 QuickCheck properties.
- `cargo fmt --check && cargo test && cargo clippy --all-targets -- -D warnings` in `rust-fuel-engine`: passed, 23 tests plus clippy.

## C#

- Overall verdict: C# is the easiest implementation to put into a C#/.NET-heavy business application. The result is organized, well tested, and integration-friendly, but it relies more on tests and runtime guards than on compiler exhaustiveness.
- Strongest parts: sealed record hierarchies for decisions, duplicate checks, previous outcomes, and vehicle lookup results; clean application DTO mapper; broad xUnit coverage for recovery, quarantine, summaries, and boundary mapping; best practical integration fit.
- Weakest parts: C# switch expressions still need default throw branches for future cases; non-empty lists and null-free references are guarded at runtime rather than fully by the type system; duplicate recovery state can still be modeled inconsistently, such as a pre-canonical failure carrying a present canonical transaction key.
- Specific correctness risks: adding a new `RowDecision`, `UploadMode`, or duplicate state will not force all switches to be updated; `QuarantinedRow` checks reason count but cannot statically prevent a null or empty reason collection; accepted duplicate transaction keys are reused from duplicate state in recovery paths, so key semantics need domain review.
- Specific slop indicators: DTO outcomes are string literals at the boundary; mapper code has repeated parsing branches; several domain alternatives are represented as sealed records but not exhaustively checked by the compiler.
- What the compiler prevents: unknown `UploadMode` enum values in normal construction; invalid subclasses outside sealed hierarchies; nullable reference warnings where enabled; basic DTO/domain type mismatches.
- What only tests/runtime checks prevent: non-empty quarantine reasons; complete handling of every decision in response mapping; no accepted transaction after fatal batch; correct recovery matrix behavior.
- How easy it was to add Quarantined: straightforward. It added a new sealed `RowDecision` case, a quarantine policy, summary count, mapper case, and focused tests, with minimal project churn.
- How easy it was to evolve Recovery mode: straightforward but test-dependent. `DuplicatePolicy` localized the change, but the compiler did not prove the matrix complete.
- How realistic the application boundary feels: strongest of the four. The DTO layer accepts nullable/raw-string application input, returns typed mapping errors, keeps parsing at the edge, and exposes response fields a C# service could use.
- Production suitability for a C#/.NET-heavy business app: highest. It is idiomatic for the shop, easy to staff, easy to debug, and fits existing .NET deployment and observability patterns.
- Reference implementation suitability: good but not ideal. It is readable and practical, but weaker at expressing invariants than Haskell or F#.
- Score rationale: full build/test credit and best practical fit; high rule correctness from 32 focused tests; lower invalid-state and change-safety scores because exhaustiveness and non-empty guarantees are not compiler-enforced.

## F#

- Overall verdict: F# produced the best .NET domain-core shape. Discriminated unions make the evolved decision model compact and reviewable while staying close enough to C# infrastructure.
- Strongest parts: `RowDecision`, `PreviousAttemptState`, `DuplicateCheckResult`, and recovery modes are explicit discriminated unions; the recovery matrix is local in `DecisionEngine`; quarantine reasons use a non-empty wrapper; domain summary is derived from classified rows.
- Weakest parts: `[<CLIMutable>]` appears on domain-facing records for interop and weakens the otherwise strong model when consumed from C#; DTO responses use `%A` formatting for several domain values; the application boundary is less polished than C#.
- Specific correctness risks: adding a new decision case gives better pattern-match feedback than C#, but interop-facing mutable records can still be constructed with null or default-ish values from C#; duplicate state no longer carries an existing transaction key, so it is less realistic for audit or idempotency behavior.
- Specific slop indicators: broad string DTO statuses; `%A`-based response text; no property tests; mutable/CLIMutable records in places where immutable domain records would normally be preferred.
- What the compiler prevents: most unhandled DU cases in pattern matches; unsupported upload modes in domain code; impossible empty quarantine reason lists through `QuarantineReasons.create`; many invalid duplicate/recovery states through a single `PreviousAttemptState` union.
- What only tests/runtime checks prevent: boundary string parsing correctness; non-empty or non-null interop fields from C# callers; warning/rejection list quality; summary and response projection correctness.
- How easy it was to add Quarantined: easy. The DU case, non-empty reason wrapper, summary handling, and tests fit naturally into the F# model.
- How easy it was to evolve Recovery mode: easy. Splitting recovery into conservative and aggressive modes was a small DU and pattern-match change with clear tests.
- How realistic the application boundary feels: adequate for a domain library, less strong as a service boundary. It exposes DTO records and a facade, but the projection still feels prototype-like.
- Production suitability for a C#/.NET-heavy business app: strong if the organization accepts an F# domain project inside a C# solution. It offers more compiler help than C# while retaining .NET deployment and calling conventions.
- Reference implementation suitability: strong. It is compact enough to serve as a reference, though Haskell is cleaner for proof-like invariants.
- Score rationale: high domain and invalid-state scores from DUs and non-empty quarantine modeling; small deductions for interop mutability, boundary formatting, fewer tests, and the successful `dotnet format` workspace warning.

## Haskell

- Overall verdict: Haskell is the best reference implementation. It gives the clearest algebraic model, the strongest non-empty guarantees, and the only property-test coverage.
- Strongest parts: algebraic data types for decisions, duplicate state, upload modes, batch outcome, validation, and fatal errors; `NonEmpty` for warnings, validation errors, quarantine reasons, fatal batch errors, and other required payloads; warnings enabled in Cabal; Hspec plus QuickCheck coverage.
- Weakest parts: poor practical fit for a C#/.NET-heavy business app; DTO boundary is a typed in-process facade rather than a realistic JSON/API adapter; some response values are `show` strings; vehicle lookup omits an ambiguous-vehicle domain case present in other implementations.
- Specific correctness risks: `PreviousAttempt` is a record of independent canonicalization and finalization states, so combinations like failed-before-canonicalization plus finalized can still be built; `skipReasonForPreviousAttempt` can produce imprecise skip reasons for some combinations; fatal DTO mapping discards supplied error detail and keeps only row number.
- Specific slop indicators: `String` remains common in DTOs and newtypes; response DTOs use `show`; the boundary is less application-realistic than C#; no formatter/linter command comparable to Rust clippy was run.
- What the compiler prevents: missing constructors in most case expressions; empty warnings/reasons/errors where `NonEmpty` is used; invalid row-decision variants; raw nulls; many accidental type mixups through newtypes.
- What only tests/runtime checks prevent: validity of primitive newtype contents; impossible cross-field combinations inside `PreviousAttempt`; DTO string parsing; production serialization behavior.
- How easy it was to add Quarantined: very easy. It was a natural `RowDecision` constructor with `NonEmpty QuarantineReason`, summary updates, and focused examples/properties.
- How easy it was to evolve Recovery mode: easy. New `UploadMode` constructors and duplicate-policy cases were local, readable, and type checked.
- How realistic the application boundary feels: weakest among serious candidates. It proves the edge can be typed, but it does not feel like an actual service adapter a .NET business app would own.
- Production suitability for a C#/.NET-heavy business app: low. It would add operational and hiring friction even though the core model is excellent.
- Reference implementation suitability: highest. This is the best version to use as a correctness oracle or design reference for another implementation.
- Score rationale: top domain, change-safety, and idiomatic scores from ADTs, `NonEmpty`, warnings, and property tests; deductions for practical fit, boundary realism, omitted ambiguous vehicle handling, and remaining cross-field duplicate-state risk.

## Rust

- Overall verdict: Rust is strong and disciplined, with excellent local tooling feedback. It prevents broad classes of slop, but the model still leaves some domain invariants to convention and is less practical for a C#/.NET shop.
- Strongest parts: enums for decisions, duplicate state, vehicle lookup, upload modes, odometer and merchant optionality; exhaustive `match`; no nulls; `cargo fmt`, tests, and clippy all pass; application boundary maps edge `Option` and strings into domain types.
- Weakest parts: fuel quantities, costs, and unit costs use `f64`; warning and validation payloads are plain `Vec`s, allowing empty warning/rejection states in public constructors; duplicate attempt state still has independently combinable flags.
- Specific correctness risks: public enum variants can be manually constructed with empty warning vectors or inconsistent duplicate-state combinations; `f64` can represent rounding-sensitive money/fuel values despite finite checks in validation; boundary response strings use debug formatting.
- Specific slop indicators: debug-formatted DTO reasons; raw string DTO outcomes; no property tests; some boundary parsing repetition.
- What the compiler prevents: unhandled enum cases in matches; null references; accidental mixing of many primitive identifiers through newtypes; many impossible optional states through enums instead of nullable fields.
- What only tests/runtime checks prevent: non-empty warning/reason vectors except quarantine; finite numeric values; consistent duplicate state combinations; exact summary and uploadability behavior.
- How easy it was to add Quarantined: easy. The enum case, `QuarantineReasons` wrapper, summary, and tests fit naturally.
- How easy it was to evolve Recovery mode: easy. Exhaustive matching made the recovery split explicit, and clippy/test feedback was fast.
- How realistic the application boundary feels: decent, but library-shaped. It is clear and typed, yet lacks serialization integration and feels less natural for a .NET application stack.
- Production suitability for a C#/.NET-heavy business app: moderate to low. Technically solid, but FFI/service boundaries and team familiarity would dominate the decision.
- Reference implementation suitability: good. It is precise and mechanically checked, but Haskell is clearer as the reference model.
- Score rationale: full build/tooling credit and high idiomatic score; deductions for `f64`, public empty vectors, cross-field duplicate-state combinations, no properties, and lower .NET practical fit.

## Cross-language findings

1. Which language produced the best domain model?
   Haskell. It had the clearest algebraic shape and the broadest use of `NonEmpty`. F# was close and is the best .NET-native model.

2. Which language produced the most practical result for a C#/.NET shop?
   C#. It fits the existing ecosystem, tooling, deployment, debugging, and staffing assumptions with the least friction.

3. Which language most reduced LLM slop?
   Haskell, followed by F# and Rust. Haskell made sloppy missing-case and empty-payload mistakes harder to express; Rust gave the best tooling enforcement; F# gave the best .NET-friendly type shape.

4. Which language had the best test story?
   Haskell. It passed 28 examples plus 4 QuickCheck properties. C# had the largest example-test count, but no property tests.

5. Which language had the worst tooling or build risk?
   Haskell. It passed locally, but Cabal/GHC is the least likely target toolchain to already exist in a C#/.NET business environment. F# also had a successful `dotnet format` run with a workspace-load warning.

6. Which language would you actually use for the fuel-upload core?
   F# if a small F# domain project is acceptable inside the .NET solution. Otherwise C# is the pragmatic production choice.

7. Which language would you use as a reference implementation only?
   Haskell. It is the clearest correctness reference and least practical production fit for this organization shape.

8. Did the functional paradigm help, or did the LLM just write functional-looking code?
   It helped when the implementation used real algebraic types and non-empty structures. Haskell and F# gained meaningful compiler pressure. The benefit was not just functional-looking syntax, although both still left some edge DTO and primitive-value validation to runtime.

9. Did idiomatic structure change the v1 conclusion?
   Yes, partially. V1 made several languages look close on a small pure kata. V2 rewarded languages that made evolution local and reviewable. F# improved as a practical .NET compromise, C# improved through project shape and boundary realism, and Haskell remained the best reference model rather than the best production choice.

10. Which implementation would be easiest for a junior developer to safely modify?
    C# for a C#/.NET shop. The code is familiar and well covered by tests. If the junior developer is comfortable with F#, F# is safer for domain-rule changes because discriminated-union pattern matching gives stronger feedback.
