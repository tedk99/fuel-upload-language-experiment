# Phase v3-4 Notes

## What changed

- Added operational batch report projection for C#, F#, Haskell, and Rust only.
- Reports carry typed ready/fatal status, existing decision-derived summary counts, uploaded transaction ids, rejected row numbers, skipped duplicate row numbers, quarantined row numbers with typed reasons, and fatal errors.
- Uploaded transaction ids are projected only from accepted/accepted-with-warning decisions and are suppressed for fatal batches.
- Added focused tests in all four target languages, including direct `BatchDecision` construction to verify the projector does not reclassify or inspect raw request rows.
- Rust accepted transactions now expose a `TransactionId` and source row number so the operational report can return real uploaded transaction ids and quarantine row numbers.

## What passed

- C#: `dotnet test csharp-fuel-engine/FuelUploadEngine.slnx` passed, 54 tests.
- F#: `dotnet test fsharp-fuel-engine/FSharpFuelEngine.slnx` passed, 42 tests.
- Haskell: `cabal test` passed, 50 examples and 0 failures.
- Rust: `cargo test` passed, including the new report tests.

## Could not verify

- No unavailable or failing target toolchains were encountered.
