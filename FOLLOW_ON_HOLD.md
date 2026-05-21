# Follow-On Language Work On Hold

This branch contains partial follow-on work for:

- Scala 3
- OCaml
- ReScript

Status:

- Tooling was prepared enough to start the work:
  - Scala CLI `1.14.0`, default Scala `3.8.3`
  - OCaml `5.2.1` through opam switch `5.2.1`
  - Dune `3.23.1`
  - ReScript `12.3.0`
- The implementation workers were intentionally stopped before completion.
- The folders in this branch should be treated as scratch/partial work, not audited experiment results.

When resuming:

1. Start from this branch.
2. Remove generated build artifacts before finalizing.
3. Complete each language in an isolated folder.
4. Run the same central verification and audit process used for the first cut.
5. Merge back to `main` only after Scala 3, OCaml, and ReScript are complete and documented.

