# V2 Scoring Rubric

Total: 100 points.

| Category | Points | What Good Looks Like |
|---|---:|---|
| Build/test reliability | 15 | Build, typecheck, format, lint, and tests run predictably with documented commands. Failures are actionable and not dependent on hidden local state. |
| Domain model quality | 20 | Core concepts are named in domain language and represented with appropriate language features such as records, enums, discriminated unions, sealed hierarchies, or algebraic data types. |
| Invalid-state prevention | 15 | Impossible or unsupported states are prevented by construction where practical, especially around decisions, duplicate state, retryability, quarantine, and fatal outcomes. |
| Rule correctness | 20 | Shared rules and phase-specific changes are implemented completely, covered by focused tests, and not weakened by later phases. |
| Change safety | 15 | New behavior can be added locally with compiler or test feedback. Decision handling is exhaustive, rule changes are easy to review, and regressions are hard to hide. |
| Idiomatic project shape | 10 | Files, modules, packages, namespaces, and test layout match normal ecosystem expectations without over-engineering the small domain. |
| Practical integration fit | 5 | Application-boundary code can accept DTO-shaped inputs and produce integration-friendly outputs while keeping the domain core pure and strongly modeled. |

## Slop Indicators

These indicators should reduce scores when they appear in domain or rule code:

- magic strings for decisions, statuses, reasons, or modes;
- booleans representing domain states;
- nullable, `Maybe`, or `Option` fields where a sum type would better express the alternatives;
- mutable counters for summaries that could be derived from decisions;
- duplicated rule logic across decision paths, tests, or mappers;
- non-exhaustive decision handling;
- weak tests that only check happy paths.
