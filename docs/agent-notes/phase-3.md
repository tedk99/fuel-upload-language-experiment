# Phase 3 Notes

Scope: Recovery mode evolution in C#, F#, Haskell, and Rust fuel engines.

Changes:
- Replaced the single recovery upload mode with explicit `ConservativeRecovery` and `AggressiveRecovery` modes.
- Preserved old recovery behavior in conservative mode: only duplicates failed before canonical finalization are accepted.
- Added typed duplicate state where needed to distinguish failed-after-canonicalization attempts with a canonical transaction key from attempts without one.
- Allowed aggressive recovery to accept failed-after-canonicalization duplicates only when no canonical transaction key exists.
- Kept normal, retry, summary, and quarantine behavior unchanged outside rows affected by the new recovery modes.

Verification:
- `dotnet test csharp-fuel-engine/FuelUploadEngine.slnx` passed.
- `dotnet test fsharp-fuel-engine/FSharpFuelEngine.slnx` passed.
- `cabal test all` passed.
- `cargo test` passed.
- `dotnet format csharp-fuel-engine/FuelUploadEngine.slnx --verify-no-changes --verbosity minimal` passed.
- `dotnet format fsharp-fuel-engine/FSharpFuelEngine.slnx --verify-no-changes --verbosity minimal` passed with workspace-load warnings only.
- `cargo fmt --check` passed.

Toolchain failures: none.
