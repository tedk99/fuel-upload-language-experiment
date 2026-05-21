# Phase 2 Prompt Log

- Phase: Quarantined outcome
- Date: 2026-05-21
- Branch: `experiment/v2-idiomatic-evolution`
- Agent: Phase 2 subagent

## Exact Subagent Prompt

````text
You are Phase 2 subagent for repo /home/ted/Develop/experiment on branch experiment/v2-idiomatic-evolution. You are not alone in the codebase; do not revert edits made by others. Work only on PHASE 2: Add Quarantined outcome. Make the smallest coherent change.

Scope ownership:
- You may edit csharp-fuel-engine, fsharp-fuel-engine, haskell-fuel-engine, rust-fuel-engine.
- You may edit docs/v2-agent-log.md and create docs/agent-notes/phase-2.md.
- Do not edit TypeScript or PureScript.

Task:
Add a new domain outcome to all four target languages: Quarantined.

Business meaning:
A row is syntactically valid and may even have a matched vehicle, but it has suspicious characteristics that require manual review before upload.

Rules:
1. Quarantined rows do not upload.
2. Quarantined rows do not block the batch.
3. Quarantined rows must appear in the batch summary.
4. Quarantined rows must have at least one typed quarantine reason.
5. Quarantine reasons must not be raw strings.
6. Warnings are separate from quarantine reasons.
7. Fatal errors still block the batch.
8. Validation errors still reject the row, not quarantine it.
9. Duplicate skip rules still run as before.
10. Do not allow Quarantined with no reasons if the language can prevent it.

Add quarantine policy with typed reasons such as:
- SuspiciousMerchantName
- SuspiciousQuantityPattern
- SuspiciousCostPattern
Use simple deterministic sample rules, e.g. merchant name contains test, unknown, or manual; quantity exactly equals a configured suspicious quantity; total cost exactly equals a configured suspicious cost.
Add config fields as needed.

Expected flow:
- Fatal/config errors first.
- Validation errors reject.
- Vehicle not found/ambiguous reject.
- Duplicate rules apply.
- If otherwise uploadable but quarantine policy triggers, return Quarantined.
- If not quarantined, accepted or accepted with warnings.

Tests required in all four languages:
- quarantined row does not upload
- quarantined row does not block batch
- quarantined row appears in summary
- quarantined row has typed reason
- fatal row still blocks batch
- validation error is rejected, not quarantined
- duplicate normal mode is skipped, not quarantined
- warning does not become quarantine

Run builds/tests for all four target languages if possible. Record missing/failing toolchains in docs/v2-agent-log.md and docs/agent-notes/phase-2.md.

Update docs/v2-agent-log.md. Commit exactly your coherent Phase 2 work with message: feat: add quarantined decision outcome across target languages

Final response: include commit SHA, tests/builds run and results, files changed summary, and any toolchain failures.
````

## Follow-Up Prompts

None.

