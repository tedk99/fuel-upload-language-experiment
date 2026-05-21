# Phase 3 Prompt Log

- Phase: Recovery mode evolution
- Date: 2026-05-21
- Branch: `experiment/v2-idiomatic-evolution`
- Agent: Phase 3 subagent

## Exact Subagent Prompt

````text
You are Phase 3 subagent for repo /home/ted/Develop/experiment on branch experiment/v2-idiomatic-evolution. You are not alone in the codebase; do not revert edits made by others. Work only on PHASE 3: Recovery mode evolution. Make the smallest coherent change.

Scope ownership:
- You may edit csharp-fuel-engine, fsharp-fuel-engine, haskell-fuel-engine, rust-fuel-engine.
- You may edit docs/v2-agent-log.md and create docs/agent-notes/phase-3.md.
- Do not edit TypeScript or PureScript.

Task:
Evolve Recovery mode to test change safety.

Current mode: UploadMode has Normal, Retry, Recovery.
New model: Recovery must have two submodes:
- ConservativeRecovery
- AggressiveRecovery

Business rules:
1. ConservativeRecovery preserves the old Recovery behavior: may accept a duplicate only if the previous attempt failed before canonical finalization.
2. AggressiveRecovery may accept a failed-after-canonicalization row only if no canonical transaction key exists.
3. AggressiveRecovery must still skip if a canonical transaction key exists.
4. Normal mode behavior unchanged.
5. Retry mode behavior unchanged.
6. Summary behavior unchanged except for rows whose decision changes because of the new recovery mode.
7. Quarantine behavior from Phase 2 must still work.

Design guidance:
Do not bolt this on with booleans like IsAggressiveRecovery. Use a typed model. Use whichever shape is idiomatic per language: Recovery of RecoveryStrategy or explicit UploadMode cases ConservativeRecovery/AggressiveRecovery.

Duplicate state may need to distinguish:
- finalized with canonical transaction key
- failed after canonicalization with canonical transaction key
- failed after canonicalization without canonical transaction key
- failed before canonical finalization
- retryable failure
- non-retryable failure
Avoid nullable transaction key if a typed duplicate state can model this better.

Tests required in all four languages:
- ConservativeRecovery preserves previous accepted case.
- ConservativeRecovery skips failed-after-canonicalization.
- AggressiveRecovery accepts failed-after-canonicalization only when no canonical transaction key exists.
- AggressiveRecovery skips failed-after-canonicalization when canonical transaction key exists.
- Normal mode still skips duplicates.
- Retry mode still only accepts explicitly retryable failures.
- Quarantined behavior still works under recovery modes.

Run builds/tests for all four target languages if possible. Record missing/failing toolchains in docs/v2-agent-log.md and docs/agent-notes/phase-3.md.

Update docs/v2-agent-log.md. Commit exactly your coherent Phase 3 work with message: feat: split recovery into conservative and aggressive modes

Final response: include commit SHA, tests/builds run and results, files changed summary, and any toolchain failures.
````

## Follow-Up Prompts

None.

