# Phase 2 Notes

Scope: C#, F#, Haskell, and Rust fuel engines only.

Implemented `Quarantined` as a typed row decision in all target languages. Quarantined rows carry typed reasons (`SuspiciousMerchantName`, `SuspiciousQuantityPattern`, `SuspiciousCostPattern`), do not contribute uploadable transactions, do not block the batch, and are counted in batch summaries.

Policy rules are deterministic:

- Merchant name contains `test`, `unknown`, or `manual`.
- Quantity equals the configured suspicious quantity.
- Total cost/amount equals the configured suspicious cost.

Validation and duplicate behavior remain ahead of quarantine:

- Fatal lookup/duplicate errors still block the batch.
- Validation errors reject the row instead of quarantining it.
- Normal-mode duplicates are skipped before quarantine policy runs.
- Warnings stay typed separately from quarantine reasons.

Test/build results:

- `dotnet test csharp-fuel-engine/FuelUploadEngine.slnx`: passed, 23 tests.
- `dotnet test fsharp-fuel-engine/FSharpFuelEngine.slnx`: passed, 14 tests.
- `cabal test all`: passed, 22 examples plus QuickCheck properties.
- `cargo test`: passed, 17 integration tests plus doc/unit test harnesses.

No missing or failing toolchains.
