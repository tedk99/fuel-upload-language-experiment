# Phase 5 Notes

Scope: cross-language scoring report for the active V2 targets only: C#, F#, Haskell, and Rust.

Files changed:

- Created `docs/v2-results.md`.
- Updated `README.md` with a short link to the results.
- Updated `docs/v2-agent-log.md`.
- Created this note.

Implementation code changes: none.

## Validation

- `dotnet test csharp-fuel-engine/FuelUploadEngine.slnx`: passed, 32 tests.
- `dotnet format csharp-fuel-engine/FuelUploadEngine.slnx --verify-no-changes --verbosity minimal`: passed.
- `dotnet test fsharp-fuel-engine/FSharpFuelEngine.slnx`: passed, 20 tests.
- `dotnet format fsharp-fuel-engine/FSharpFuelEngine.slnx --verify-no-changes --verbosity minimal`: passed, with the warning `Warnings were encountered while loading the workspace. Set the verbosity option to the 'diagnostic' level to log warnings.`
- `cabal test all` from `haskell-fuel-engine`: passed, 28 examples and 4 QuickCheck properties.
- `cargo fmt --check && cargo test && cargo clippy --all-targets -- -D warnings` from `rust-fuel-engine`: passed, 23 tests plus clippy.

Tool versions observed:

- .NET SDK `10.0.107`
- Cabal `3.14.2.0`
- GHC `9.6.7`
- rustc `1.95.0`
- cargo `1.95.0`

## Scoring Summary

| Rank | Language | Score | Summary |
|---:|---|---:|---|
| 1 | Haskell | 90 | Best reference model and test story, weakest practical .NET fit. |
| 2 | F# | 89 | Best .NET-oriented domain model, with rougher DTO projection. |
| 3 | C# | 88 | Best practical production fit, weaker compiler exhaustiveness. |
| 4 | Rust | 87 | Strong tooling and enums, lower .NET fit and some public invalid states. |

## Notes

- No toolchain was missing or failing.
- The F# format command succeeded but should be treated as having a workspace warning.
- The report intentionally did not score TypeScript or PureScript because Phase 5 scope limited scoring to C#, F#, Haskell, and Rust.
