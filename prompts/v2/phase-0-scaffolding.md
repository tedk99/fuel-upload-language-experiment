# Phase 0 Prompt Log

- Phase: Experiment scaffolding
- Date: 2026-05-21
- Branch: `experiment/v2-idiomatic-evolution`
- Agent: Phase 0 subagent

## Exact Subagent Prompt

````text
You are Phase 0 subagent for repo /home/ted/Develop/experiment on branch experiment/v2-idiomatic-evolution. You are not alone in the codebase; do not revert edits made by others. Work only on PHASE 0: Experiment scaffolding.

Required changes:
- Create docs/v2-experiment-plan.md explaining why v1 was useful but kata-shaped, why v2 focuses on evolution/change safety, target languages C#, F#, Haskell, Rust, frozen/baseline TypeScript and PureScript if present, and the five phases: Idiomatic project structure, Quarantined outcome, Recovery mode evolution, Application boundary / DTO mapping, Cross-language scoring report.
- Create docs/v2-scoring-rubric.md with total 100 and these categories: Build/test reliability 15, Domain model quality 20, Invalid-state prevention 15, Rule correctness 20, Change safety 15, Idiomatic project shape 10, Practical integration fit 5. Define slop indicators: magic strings, booleans representing domain states, nullable/Maybe/Option fields where sum types would be better, mutable counters, duplicated rule logic, non-exhaustive decision handling, weak tests that only check happy paths.
- Create docs/v2-agent-log.md starting with the table header: | Phase | Agent | Commit | Languages touched | Tests run | Notes | and separator row.
- Update README.md with a short v2 experiment section linking those docs.
- Run lightweight validation available for docs/repo state.
- Commit exactly your coherent Phase 0 work with message: docs: add v2 idiomatic evolution experiment plan
- Leave a short markdown note describing what changed, what passed, and what could not be verified. Put it under docs/agent-notes/phase-0.md.

Do not modify target language implementations. Final response: include commit SHA, tests/validation run, and files changed.
````

## Follow-Up Prompts

None.

