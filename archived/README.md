# Archived Projects

These projects are V1 implementations that were not evolved in the V2 experiment. They are valid comparison baselines for V1 but do not implement:

- The `Quarantined` outcome (added in V2 phase 2)
- Split recovery modes (`ConservativeRecovery` / `AggressiveRecovery`, added in V2 phase 3)
- The application boundary / DTO mapping layer (added in V2 phase 4)

## Why frozen

The V2 experiment plan designated C#, F#, Haskell, and Rust as active targets. TypeScript and PureScript were kept as frozen V1 baselines for cross-experiment comparison only. Neither language was opened for V2 evolution work.

## Contents

| Folder | Language | V1 Score | Build |
|---|---|---|---|
| `typescript-fuel-engine` | TypeScript 5.4+, strict mode | 88 | `npm run typecheck && npm test && npm run build` |
| `purescript-fuel-engine` | PureScript 0.15.16 | 86 | `spago test` |

Both passed build and test at V1 completion. PureScript requires `NODE_OPTIONS=--dns-result-order=ipv4first` due to IPv6 DNS issues with the Spago registry.

See `../docs/v2-experiment-plan.md` for the full V2 target rationale, and `../README.md` for the original V1 scoring table.
