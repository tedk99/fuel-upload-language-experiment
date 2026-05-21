# Normal C# Fuel Engine

A deliberately *junior-to-mid level* C# implementation of the fuel upload
engine. Written the way most teams' code actually looks: mutable POCOs,
string-typed status values, exceptions as control flow, `null` everywhere,
no NRT.

It exists to be **compared** against the more idiomatic / advanced
implementations in this repo, as a teaching tool for Paul Graham's "blub
paradox" — the idea that you can't see what your current language is missing
until you've used a more expressive one.

## Run

```bash
dotnet build NormalCSharpFuelEngine.slnx
dotnet test  NormalCSharpFuelEngine.slnx
```

10 tests pass. 6 are happy-path; **4 of them pin known bugs** so juniors can
see the failure modes the type system isn't catching.

## Read

The teaching is in the docs:

- [`docs/01-walkthrough.md`](docs/01-walkthrough.md) — structure, flow, and
  the seven footguns that this version makes very easy to step in.
- [`docs/02-vs-csharp-idiomatic.md`](docs/02-vs-csharp-idiomatic.md) — how
  the idiomatic C# branch closes each footgun (and what it costs).
- [`docs/03-vs-fsharp.md`](docs/03-vs-fsharp.md) — moving from C# to F#.
- [`docs/04-vs-haskell.md`](docs/04-vs-haskell.md) — the type-driven extreme.
- [`docs/05-vs-rust.md`](docs/05-vs-rust.md) — different goals, same wins.
