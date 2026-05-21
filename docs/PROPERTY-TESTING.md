# Property-Based Testing

A primer for engineers new to property-based testing, grounded in this repo's
fuel-upload domain. Two implementations have property tests today:

- Haskell: [`haskell-fuel-engine/test/FuelUpload/Properties.hs`](../haskell-fuel-engine/test/FuelUpload/Properties.hs) (QuickCheck)
- F#: [`fsharp-fuel-engine/FuelUpload.Domain.Tests/Properties.fs`](../fsharp-fuel-engine/FuelUpload.Domain.Tests/Properties.fs) (FsCheck)

Both libraries are descendants of the same idea — QuickCheck, originally from
Haskell — so the mental model transfers cleanly between them.

---

## The shift in mindset

Normal unit tests are **example-based**: one specific scenario, one specific
input, one specific expected output.

```fsharp
[<Fact>]
let ``valid normal row is accepted without warnings`` () =
    let decision = classify UploadMode.Normal (row 1) matched noDuplicate

    match decision with
    | RowDecision.Accepted transaction ->
        Assert.Equal(1, transaction.SourceRowNumber)
    | other -> failwith $"Expected accepted row, got %A{other}"
```

You're saying: *"For this exact row, the answer should be Accepted."* If the
code breaks for some other row you didn't think of, the test won't catch it.

**Property-based tests** flip this around. Instead of writing specific
examples, you state a **rule that should hold for every possible input**, and
the library generates hundreds of random inputs and tries to break it. When it
finds a failure it **shrinks** it down to the smallest input that still
reproduces the bug.

```fsharp
[<Property(Arbitrary = [| typeof<DomainGenerators> |])>]
let ``summary count partitions always add up to total`` (decisions: RowDecision list) =
    let classified =
        decisions
        |> List.mapi (fun i d -> { Row = baseRow (i + 1); Decision = d })

    let s = BatchSummary.summarize classified

    s.AcceptedRows
    + s.QuarantinedRows
    + s.SkippedDuplicateRows
    + s.RejectedRows
    + s.FatalErrorRows = s.TotalRows
```

You're saying: *"No matter what list of decisions you throw at me, the
partition counts must sum to the total."* FsCheck generates ~100 random lists
per run and tries to find one where the equality fails.

---

## The two parts you write

Property tests have two halves:

### 1. Generators (`Gen<'T>`)

Recipes for producing random domain values. You have to teach the library
what a realistic input looks like — it can't guess your domain rules.

```fsharp
let private fatalGen : Gen<FatalProcessingError> =
    Gen.elements
        [ FatalProcessingError.VehicleLookupUnavailable "lookup down"
          FatalProcessingError.DuplicateCheckUnavailable "duplicate store timed out" ]

let private fatalContextGen : Gen<FuelRowContext> =
    gen {
        let! n = rowNumberGen
        let! fatal = fatalGen
        let! viaDuplicate = Gen.elements [ true; false ]
        let row = baseRow n

        if viaDuplicate then
            return
                { Row = row
                  VehicleLookup = VehicleLookupResult.Matched vehicle
                  DuplicateCheck = DuplicateCheckResult.Fatal fatal }
        else
            return
                { Row = row
                  VehicleLookup = VehicleLookupResult.Fatal fatal
                  DuplicateCheck = DuplicateCheckResult.NoDuplicate }
    }
```

Then you register them so the test framework knows which generator to use
for each parameter type:

```fsharp
type DomainGenerators =
    static member Decisions() = Arb.fromGen (Gen.listOf decisionGen)
    static member NonEmptyFatalContexts() = Arb.fromGen nonEmptyFatalContextsGen
    static member AcceptedRow() = Arb.fromGen acceptedRowGen
```

The Haskell version does the same thing via type-class instances:

```haskell
newtype Decisions = Decisions [RowDecision]
  deriving stock (Show)

instance Arbitrary Decisions where
  arbitrary = Decisions <$> listOf arbitraryDecision
```

### 2. Properties

Short boolean assertions that should hold for **every** generated input.

```fsharp
[<Property(Arbitrary = [| typeof<DomainGenerators> |])>]
let ``fatal row decisions block the batch`` (contexts: FuelRowContext list) =
    match DecisionEngine.classifyBatch config UploadMode.Normal contexts with
    | BatchDecision.Blocked _ -> true
    | BatchDecision.Ready _ -> false
```

The generator guarantees every list contains at least one fatal context. The
property asserts: classification of such a batch must always produce
`Blocked`. If a future change accidentally lets a fatal slip into a `Ready`
batch, this test catches it across hundreds of arrangements (fatal at the
start, end, middle, surrounded by N other rows).

---

## The four properties in this repo

| # | Property | Why it matters |
|---|---|---|
| 1 | `summary total = decision count` | Sanity invariant. If anyone drops a row during summarization, this catches it. |
| 2 | `partitions sum to total` | Every decision lands in exactly one bucket. Catches double-counting or missed cases (e.g. someone adds a new `RowDecision` variant and forgets to update the summary folder). |
| 3 | `a batch containing any fatal blocks the whole batch` | Core business rule from [`CLAUDE.md`](../CLAUDE.md). Verified across hundreds of random arrangements. |
| 4 | `accept-eligible rows are never rejected` | Generates rows with valid volumes/costs/dates and verifies the classifier always produces `Accepted` or `AcceptedWithWarnings`. Catches accidental tightening of validation. |

The F# implementations live in [`Properties.fs`](../fsharp-fuel-engine/FuelUpload.Domain.Tests/Properties.fs);
the Haskell originals live in [`Properties.hs`](../haskell-fuel-engine/test/FuelUpload/Properties.hs).
They are intentionally parallel, so you can read both side by side to see how
the same idea ports across languages.

---

## Where property tests shine vs where they don't

**Great for:**

- **Invariants** — totals, conservation laws ("partitions sum to total").
- **Round-trips** — `parse >> serialize = id`, `encode >> decode = id`.
- **State machines** — every sequence of operations leaves the system in a
  valid state.
- Anywhere the rule is naturally phrased as *"for all X, …"*.

**Not great for:**

- **Specific business decisions** — "a row from Vendor X at $99 should
  quarantine". Keep example-based tests for those; they're more readable as
  documentation and tell future readers *why* this exact case matters.
- **Things with complex setup** — if generating a valid input takes 50 lines
  of generator code, an example test is clearer.

The two styles complement each other. Look at
[`Tests.fs`](../fsharp-fuel-engine/FuelUpload.Domain.Tests/Tests.fs) and
[`Properties.fs`](../fsharp-fuel-engine/FuelUpload.Domain.Tests/Properties.fs):
the example tests pin down specific business outcomes ("retry mode accepts only
explicitly retryable duplicates"), the property tests guard universal
invariants ("partitions sum to total"). Neither is sufficient on its own.

---

## Shrinking — the killer feature

When a property fails, the library doesn't just hand you the random 500-element
list it found a bug in. It **shrinks** the failing input by repeatedly trying
smaller variations, until it lands on the smallest input that still fails.

In practice this means a failure report might say "this property fails on
`[Accepted txn]`" rather than "this property fails on this 487-element list of
random decisions." That tiny reproducer is often enough to spot the bug
immediately.

You get shrinking for free as long as your generators are built from the
library's standard combinators (`Gen.listOf`, `Gen.choose`, `Gen.elements`,
`Gen.oneof`, etc.).

---

## The libraries

| Language | Library | Test runner integration |
|---|---|---|
| Haskell | `QuickCheck` + `hspec-quickcheck` | `hspec` via the `prop` combinator |
| F# | `FsCheck` + `FsCheck.Xunit` | xUnit via the `[<Property>]` attribute |

Both are descended from the original Haskell QuickCheck paper (Claessen &
Hughes, 2000). Most modern languages have a port — Python (`hypothesis`), Rust
(`proptest`, `quickcheck`), TypeScript (`fast-check`), Scala (`ScalaCheck`).
The mental model transfers; the syntax is the only thing that changes.

---

## Further reading

- [QuickCheck paper (Claessen & Hughes, 2000)](https://www.cs.tufts.edu/~nr/cs257/archive/john-hughes/quick.pdf) — the original.
- [Choosing properties for property-based testing](https://fsharpforfunandprofit.com/posts/property-based-testing-2/) — Scott Wlaschin's catalogue of property patterns ("there and back again", "different paths, same destination", etc.). Excellent for getting unstuck when you can't think of a property to write.
