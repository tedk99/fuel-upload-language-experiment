# V3 Scoring Rubric

Total: 100 points.

| Category | Points | What Good Looks Like |
|---|---:|---|
| Build/test reliability | 15 | Build, typecheck, format, lint, and tests run predictably with documented commands. Failures are actionable and not dependent on hidden local state. |
| Domain boundary integrity | 20 | CSV parsing, DTO mapping, audit projection, repository ports, and reporting stay outside the pure domain rules. Domain types remain named in business language instead of integration language. |
| Typed integration errors | 15 | Import, mapping, lookup, persistence, and projection failures are represented explicitly with typed errors or results rather than exceptions, nulls, raw strings, or catch-all failure buckets. |
| Rule correctness preservation | 20 | Existing duplicate, retry, recovery, quarantine, rejection, warning, fatal, and summary rules remain correct while integration-shaped features are added. Tests cover both preserved rules and new edge cases. |
| Change safety | 15 | New integration behavior can be added locally with compiler or test feedback. Decision handling is exhaustive, projections are derived from canonical decisions, and regressions are hard to hide. |
| Idiomatic integration shape | 10 | Modules, ports, adapters, tests, and project structure match normal ecosystem expectations without over-engineering the small experiment. |
| Practical production fit | 5 | The result is plausible production-facing code: observable, testable, reviewable, and clear about where real CSV, persistence, and reporting infrastructure would attach. |

## Slop Indicators

These indicators should reduce scores when they appear in domain, integration, or rule code:

- CSV parsing leaking into domain logic;
- raw string statuses in domain code;
- untyped mapping errors;
- nullable, `Maybe`, or `Option` fields where sum types would be better;
- duplicated decision-to-report logic;
- mutable counters that can drift;
- repository interfaces that expose persistence details to the domain;
- weak tests that only check happy paths.
