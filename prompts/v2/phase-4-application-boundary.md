# Phase 4 Prompt Log

- Phase: Application boundary / DTO mapping
- Date: 2026-05-21
- Branch: `experiment/v2-idiomatic-evolution`
- Agent: Phase 4 subagent

## Exact Subagent Prompt

````text
You are Phase 4 subagent for repo /home/ted/Develop/experiment on branch experiment/v2-idiomatic-evolution. You are not alone in the codebase; do not revert edits made by others. Work only on PHASE 4: Application boundary / DTO mapping. Make the smallest coherent change.

Scope ownership:
- You may edit csharp-fuel-engine, fsharp-fuel-engine, haskell-fuel-engine, rust-fuel-engine.
- You may edit docs/v2-agent-log.md and create docs/agent-notes/phase-4.md.
- Do not edit TypeScript or PureScript.

Task:
Add a realistic application boundary for each target language. The pure domain core should remain clean, but real apps need DTOs at the edge.

For each target language, add a small boundary layer mapping:
External request DTO -> domain request
domain decision -> response/audit DTO

No DB, HTTP server, CSV parser, logging, or framework dependencies. Mapping layer only.

C#:
Add Application or Interop folder with:
- FuelUploadRequestDto
- FuelUploadRowDto
- FuelUploadResponseDto
- FuelUploadDecisionDto
- FuelUploadMapper
- FuelUploadApplicationService or Facade
The service may call the pure engine but must not contain business rules.

F#:
Add an interop/facade module:
- DTO records for C#-friendly calls
- Mapper functions
- Facade function/class if useful
Keep F# core idiomatic; put C# friendliness at the boundary.

Haskell:
Add a simple API module:
- request/response DTO-like types
- mapping functions
- no JSON dependency unless already present and trivial

Rust:
Add boundary module:
- request/response structs
- From/TryFrom impls where useful
- no web framework
- no serde unless already present or clearly justified

Rules:
- Mapping errors must be typed.
- Boundary DTOs may be more primitive/stringly than domain types, but mapping must validate or reject them.
- Do not leak internal domain representation if a cleaner external decision shape is better.
- Keep business rules in the domain engine, not the mapper.

Tests required in all four languages:
- valid DTO maps and classifies
- invalid DTO produces typed mapping error
- response DTO correctly represents accepted/rejected/skipped/quarantined/fatal decisions
- mapper does not recompute summary independently

Run builds/tests for all four target languages if possible. Record missing/failing toolchains in docs/v2-agent-log.md and docs/agent-notes/phase-4.md.

Update docs/v2-agent-log.md. Commit exactly your coherent Phase 4 work with message: feat: add application boundary DTO mapping for target languages

Final response: include commit SHA, tests/builds run and results, files changed summary, and any toolchain failures.
````

## Follow-Up Prompts

None.

