# Applying Correctness Lessons to a New Project

This prompt is for an LLM working in an unfamiliar codebase. The goal is to import a specific set of modeling and architecture lessons from a separate language experiment, then audit this project against them and propose one small typed slice — not a rewrite.

The lessons come from a controlled experiment that modeled the same pure-decision domain problem in C#, F#, Haskell, and Rust. The lessons are language-agnostic; they're about how to *shape* a domain so that correctness is enforced by types and so that an LLM working on the code can't trivially produce slop.

---

## 0. Discussion first (do not skip)

Do not start scanning the code. First, find out what the human is bringing to this. Ask, and listen:

1. **The project and the pain.** What's this codebase, and what specifically is slop, drift, or fragility annoying you most right now? Concrete examples beat vague complaints.
2. **Ambition and language posture.** Is this a project you'd consider rewriting parts of in another language (F#, Rust, Haskell, OCaml, etc.), or should everything stay in the current stack? Is the language choice itself something you want to experiment with, or do you want pragmatic and boring?
3. **Scope.** Do you already have a slice in mind, or do you want me to scout and propose one?
4. **Off-limits.** Is there code I should not touch — generated, vendored, in-flight on another branch, owned by another team?

The answers determine the shape of the work. Two common modes:

- **Greenfield-ish or personal project, open to language change.** The codebase is small enough to learn quickly. The human is up for experimenting. Pick a slice, propose the typed model, and feel free to recommend a new language for the slice if there's a clean seam *and* the human is up for it. Don't push a language change for elegance alone — only if it materially reduces slop or duplication.

- **Legacy bloat, must mostly stay in the current stack.** The codebase is large, knotted, and possibly poorly understood even by the people who own it. Triage first: read enough to understand what the app *actually does* in domain terms before recommending anything. The goal is one isolatable "functional core island" — a piece that can be cleaned up or extracted without forcing a full rewrite. Idiomatic re-expression in the existing language counts; introducing a small functional-language island for a hot, pure piece is also fair game if the seam is clean.

Use a structured question tool if your environment has one (e.g. Claude Code's `AskUserQuestion`). Otherwise plain prose questions are fine. Don't ask all four at once if the answers compound — ask 2, then follow up.

The wrong picked slice wastes more time than a five-minute conversation.

---

## 1. The shape: functional core, imperative shell

Gary Bernhardt's pattern, and the architectural backbone of everything that follows.

The center of the program is a **functional core**: pure functions that take typed data in and return typed decisions out. No I/O, no DB, no HTTP, no clock reads, no filesystem, no mutation, no logging side effects, no randomness. The core does not know it lives inside a web app, a CLI, a phone, or a batch job.

Around the core is a thin **imperative shell**: the HTTP handlers, the DB queries, the file reads, the clock, the message bus, the UI. The shell talks to the outside world, calls the core, and reflects the core's output back out. The shell calls the core; the core never calls the shell.

Two consequences worth internalizing:

- **The core is trivially testable.** It takes plain data, returns plain data. No mocks, no test doubles, no setup. Property tests with thousands of generated cases run in seconds. This is where the interesting bugs hide, so this is where the test budget should go.
- **The shell is allowed to be boring.** It's mostly translation: parse this JSON into typed values, hand them to the core, render the core's output back to JSON. Don't try to FP-purify your HTTP handler. Do try to keep domain decisions out of it.

The ten lessons below are about how to do the *core* part well. The shell stays imperative — that's the point.

---

## 2. The lessons (the unit-level rules for the core)

**1. Sum types are the unit of decision.** Every domain decision resolves to a single tagged value. `Outcome = Accepted | Warned | Quarantined | Skipped | Rejected | Fatal` — not `status: String` + `accepted: Bool` + `error: String?`. If a function returns "one of N outcomes," the return type is literally the sum of those N outcomes.

**2. Make illegal states unrepresentable.** An `Accepted` that carries validation errors should not compile. If an invariant is cheap to encode in the type, encode it. Variants carry the data they actually need; nothing else.

**3. Reasons are typed, never strings.** Every rejection, quarantine, or fallback carries a typed reason. `Rejected(MissingVehicle)` not `Rejected("vehicle not found")`. The boundary renders the typed reason for humans; the core never sees the string.

**4. Decision functions are pure.** No I/O, DB, HTTP, clock, mutation, or logging inside the function that makes the decision. If time matters, time is a parameter. If a lookup matters, the lookup result is a parameter. This is the lesson the shell/core split exists to enforce.

**5. Aggregates are folds, never counters.** A batch summary is derived from the list of per-row decisions. No `totalAccepted += 1` anywhere. If you find a counter stored next to the data it summarizes, ask whether it can drift from the source of truth — the answer is usually yes.

**6. DTOs live at the boundary, never in the core.** Parse external JSON / form / wire data into typed domain values at the edge, fail fast on invalid input. Render domain values back to DTOs on the way out. The core sees no `dict[str, Any]`, `JSONObject`, `[String: Any]`, or untyped payload.

**7. One rule, one place.** If the same rule lives in the API validator, the UI guard, and the persistence check, two of them will drift. Hoist the rule into one pure function and call it from each boundary.

**8. Negative space matters more than happy paths.** Tests that only assert "valid input → valid output" don't prove the rules. Property tests where possible. Otherwise, exhaustive cases over each variant of every sum type — including the suspicious, ambiguous, and fatal variants.

**9. Refinement wrappers when invariants are non-trivial.** `NonEmpty<T>`, `Positive<T>`, `EmailAddress`, `IsoDate`. Small cost, big payoff: every downstream function knows the value already satisfies the invariant, no defensive re-checking.

**10. No boolean soup.** If a condition is built from three or more booleans, the type system is missing a sum type. `if isAdmin && !isLocked && !isExpired && hasPaid` is a `UserAccess = FullAccess | Locked | Expired | Unpaid | Restricted` waiting to be born.

---

## 3. What we're avoiding (the LLM-slop angle)

LLMs produce slop in predictable shapes. Each item below is the inverse of a lesson above — flag wherever it appears:

- `status: str` returned from anything that decides
- `dict[str, Any]` / `[String: Any]` / `JSONObject` passed into pure logic
- `Optional` chains where the absent value carries semantic meaning that deserves its own variant
- `try / except` (or `try / catch`) blocks that swallow errors and continue with a default
- Multiple `if` ladders that each re-derive the same conditional from raw fields
- Tests that pass exactly one input shape and assert exactly one output shape
- Counters and totals stored next to the data they summarize
- "Validators" that return `bool` rather than parsing into a typed value
- Functions named `process` or `handle` that take eight parameters and return `None`
- I/O calls (DB, HTTP, file) inside a function that also makes a domain decision

These aren't style nits. Each is a place where two LLM sessions, or one LLM and one human, will produce inconsistent answers because the type system isn't holding the line.

---

## 4. Your task

Two paths, depending on what the discussion in §0 surfaced.

### Path A — greenfield-ish or open to language change

#### A.1 Find the decision surface

Where does this project actually *decide* things? For each surface, note: where it lives, its inputs, its outputs, and whether the outputs are a typed sum or untyped soup.

#### A.2 Pick the one decision that's worst

One slice, not all of them. Choose by: how much downstream code depends on it, how loose the current modeling is, how likely the rule is to change soon, and how testable it would become if tightly typed. Justify the pick in three to five sentences.

#### A.3 Propose the typed model

For the chosen decision:
- Current files and line ranges
- Current shape of the decision (the slop)
- Proposed typed model — show actual type definitions, with variants and typed reasons
- What at the boundaries needs to translate to and from this model
- The tests that would prove the rules

If a language change for the slice is on the table per §0, name a candidate, say *why* (clean seam, existing affinity, learning value), and acknowledge the cost (toolchain, deployment, team familiarity). Don't recommend a language change unless the seam is genuinely clean.

#### A.4 Plan the slice as four PR-sized steps

- Step 1: Introduce the new types alongside the existing code; no behavior change.
- Step 2: Write the pure decision function against the new types, with tests.
- Step 3: Replace one caller; leave the rest on the old path.
- Step 4: Migrate remaining callers; delete dead code.

If a language change is in play, Step 1 also stands up the new project skeleton and the inter-language seam (HTTP boundary, file format, shared schema — whichever fits).

### Path B — legacy bloat

#### B.1 Triage what's actually here

Before recommending anything: read enough to describe what this app *does* in domain terms a non-engineer stakeholder would recognize. Two to four paragraphs. Name the major workflows. Name the domains the app actually operates on (vehicles, fuel rows, work orders, drivers, whatever it is).

Then list the decision surfaces you found, with a one-line note per surface on how tangled it is. Don't try to map every line of code — that's a separate document. Map the *decisions*.

#### B.2 Find one isolatable functional-core island

Look for a piece with these properties:
- Clear inputs and clear outputs
- Manageable number of callers (single-digit ideally)
- Rules that the team agrees on (i.e. not under active debate)
- Currently expressed as slop (raw strings, boolean soup, `dict[str, Any]`)
- Touched often enough that the slop hurts

That's your island. Justify the pick.

#### B.3 Propose the cleanup

Two flavors are both acceptable:
- **Idiomatic re-expression in place**: same language, but the slop becomes a sum type with typed reasons, the function becomes pure, the boundary DTOs get factored out.
- **Functional-core island in a sibling language**: the pure decision moves into a small F# / Haskell / Rust / Ocaml library that the existing C# / Python / Java app calls across a clean seam (in-process where the runtimes allow, otherwise HTTP or stdio). Only suggest this if the seam is genuinely clean and the team appetite is there.

Show the proposed types either way.

#### B.4 Plan as four steps, same shape as Path A

#### B.5 Explicit no-go list for the rest of the codebase

The bloat is real but most of it isn't the problem you're solving today. List, with one-line reasons:
- Code that isn't a decision surface (rendering, layout, transport, infrastructure)
- Boundaries where slop is contained and doesn't leak inward
- Areas where rules are still in flux and typing them now is premature
- Anything where the wrapper-type cost exceeds the payoff
- Anything where the team is mid-migration on a parallel axis

---

## 5. Output

A single markdown report:

- **Discussion summary** (one paragraph): what the human said in §0 and which path you're on
- **Decision surfaces**: brief list with current shape
- **The slice you picked**, and why
- **Proposed typed model**: real type definitions, not prose
- **Four-step plan** (or longer if Path B requires a triage step)
- **Do-not-do list**
- **Risk**: low / medium / high with one sentence per risk
- **Language note** (optional): if a language change is in play, why and at what cost

Default assumption: keep the project in the language it already lives in. A language change is only worth proposing if a clean seam exists, the human is open to it per §0, and the payoff is large.
