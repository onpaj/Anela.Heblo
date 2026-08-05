# Architecture assessment — TransportBox state-machine validation reasons collapse to generic ValidationError

## Verdict

**Approved as designed, with two scope trims.** I re-verified the plan/design's factual claims
directly against the current source (`TransportBox.cs`, `TransportBoxTransition.cs`,
`TransportBoxStateNode.cs`, the four Application handlers, `ErrorCodes.cs`, `i18n.ts`,
`errorHandler.ts`, `LocalizationCoverageTests.cs`, `GetGroupMembersHandler.cs`, domain test
assertions) and everything checked out — reachability analysis, the "no `Params` substitution"
bug, the precedent pattern, the `1406` next-free slot, and the `Should().Throw<ValidationException>()`
compatibility claim are all correct as stated. No corrections to the design's factual basis are
needed. The two trims below are scope judgment calls, not corrections.

## Alignment with existing patterns

- **Exception-per-failure, caught by type, mapped to `ErrorCodes` in the handler** is an
  established idiom (`GetGroupMembersHandler.cs:34-77`, confirmed: three typed catches +
  generic fallback, most-specific-first). The design's catch-clause ordering and "generic
  `ValidationException` stays as a safety net" approach matches this precedent exactly.
- **Domain exceptions subclassing `Exception`/`ValidationException` with typed data properties**
  is precedented (`InvalidPhotoSearchPatternException` carries `Pattern`, confirmed). The design's
  four new types follow the same shape (small, single-purpose, constructor sets the message and
  the typed property).
- **Domain has no reference to Application** — confirmed via `Anela.Heblo.Domain.csproj` (only
  references `Anela.Heblo.Xcc`). This is the reason the mapping must live in the handler, not the
  domain — the design respects this correctly and never has `TransportBox.cs` touch `ErrorCodes`.
- **`formatMessage`'s single-brace-only regex** (`errorHandler.ts:14-18`, confirmed) — the design's
  choice of `{code}`/`{currentState}`/`{allowedStates}` placeholders (not `{{...}}`) is the only
  syntax that actually renders. Good catch, and correctly scoped as "don't touch the pre-existing
  double-brace bug elsewhere."
- **`ErrorCodes` module-range convention** (14XX = Transport, confirmed `1401`–`1405` in use,
  `1406` next free) and the `[HttpStatusCode(...)]` attribute convention (`BadRequest` for
  malformed input, `UnprocessableEntity` for state-machine rejections, mirroring
  `TransportBoxStateChangeError = 1402`) are both followed correctly.
- **`LocalizationCoverageTests`** (confirmed: regex-scans `i18n.ts` for `EnumName: "..."`, plus a
  module-prefix-range sanity check) needs no test-code changes — adding the four enum values +
  four i18n string entries satisfies both existing `[Fact]`s as-is.
- **Synchronous exception propagation through the transition machinery** — confirmed
  `TransportBoxTransition.ChangeStateAsync` (`:26-30`) invokes the callback synchronously inside
  `Task.FromResult`, and `TransportBoxStateNode` has no try/catch — so exceptions thrown deep in
  `box.Open()`/`box.ToTransit()`/`box.RevertToOpened()` do propagate up through
  `ChangeTransportBoxStateHandler.Handle`'s single `try` block as the design assumes. No hidden
  exception-swallowing layer exists between domain and handler.
- **`ValidationException` base-type test compatibility** — confirmed: all six `.WithMessage(...)`
  and `.Throw<ValidationException>()` assertions in `TransportBoxStateTransitionTests.cs` and
  `TransportBoxCodeCaseHandlingTests.cs` target message text, and FluentAssertions'
  `Throw<T>()` matches subtypes — so subclassing `ValidationException` and preserving message text
  verbatim (as the design specifies) keeps every existing domain test green without modification.

## Proposed architecture (confirmed)

```
TransportBox.cs (domain)
  ├─ throw TransportBoxCodeRequiredException()          [Open(), :63]
  ├─ throw TransportBoxCodeFormatException(code)         [Open(), :69]
  ├─ throw TransportBoxEmptyException(Code)               [ToTransit(), :178]
  └─ throw TransportBoxInvalidStateTransitionException(   [RevertToOpened() :88-89, CheckState() :252]
         message, currentState, allowedStates)
         ↓ (all subclass ValidationException — existing catch(ValidationException) still works)
Application handlers (4x, each catching only its reachable subset)
  ├─ ChangeTransportBoxStateHandler   → all 4 new types
  ├─ OpenOrResumeBoxByCodeHandler     → TransportBoxCodeFormatException only
  ├─ RemoveItemFromBoxHandler         → TransportBoxInvalidStateTransitionException only
  └─ AddItemToBoxHandler              → TransportBoxInvalidStateTransitionException only
         ↓ maps to ErrorCodes (1406-1409) + Params, generic ValidationException catch stays as fallback
ErrorCodes.cs — 4 new 14XX values
         ↓ OpenAPI-generated frontend enum (no manual edit)
i18n.ts — 4 new Czech translation entries, single-brace placeholders
         ↓ existing, unchanged toast pipeline (client.ts → handleApiError → getErrorMessage)
Operator sees the specific reason instead of "Chyba validace"
```

This is the right shape: it's additive, uses only already-precedented patterns, touches no
contracts, and needs zero frontend component/plumbing changes — only string data. I have no
alternative to propose; the design already converged on the only approach consistent with the
domain/Application layering constraint (Domain can't reference `ErrorCodes`, so per-type catch in
the handler is the only place the mapping can live).

## Scope trims (my calls, diverge slightly from the design)

**1. Fold `TransportBoxInvalidStateTransitionException` into fewer call sites than proposed.**
The design already correctly declined to give `RevertToOpened`'s `:94` missing-code throw its own
type (dead-reachability argument, verified sound — `Code` is always set before any of the three
states `RevertToOpened` accepts). I'd go one step further and question whether `CheckState()`'s
generic guard (`:252`, used by `AddItem`/`DecreaseItem`/`ToPick`/`Close`/etc. — effectively the
catch-all for "wrong state for this operation") truly needs a *named* exception type versus simply
improving the existing generic `ValidationException`'s `Params` payload. But the design's answer —
give it a real type, because two handlers (`AddItemToBoxHandler`, `RemoveItemFromBoxHandler`)
*do* reach it as their only operator-facing rejection today — is the more defensible call, since
"box must be Opened to add/remove items" is a genuinely common operator mistake (scanning into a
closed box), not an edge case. **Keep the design as written here** — I was second-guessing but the
design's reasoning holds once you look at who actually catches it.

**2. Don't let `OpenOrResumeBoxByCodeHandler`'s catch list expand beyond `TransportBoxCodeFormatException`.**
The design already gets this right (explicitly declines to add a `TransportBoxCodeRequiredException`
catch there since the untrimmed-whitespace pre-check makes it unreachable) — flagging only to
confirm the dev step must **not** "complete the set" by adding all 4 catches to all 4 handlers out
of consistency-mindedness. Dead catch clauses are worse than an intentionally partial set; enforce
per-handler reachability, not uniformity.

## Implementation guidance

- **New file**: `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBoxExceptions.cs`
  — four classes, exactly as drafted in `design-01.md`. Do not put these in separate files; the
  existing `InvalidPhotoSearchPatternException` precedent is one class per concept but this module
  has no established multi-file-per-exception convention to match, and four one-line files would
  be worse for review than one cohesive file.
- **`ChangeTransportBoxStateHandler.Handle`**: insert the four new `catch` blocks between the
  existing `try` body and the current `catch (ValidationException ex)` (line 149) — that fallback
  stays verbatim, unchanged, as the safety net for `:58`/`:94`/`:188`/`:193` (the confirmed-unreachable
  or intentionally-unmigrated sites).
- **Three sibling handlers**: each gets only its one reachable new catch, inserted the same way
  ahead of their existing `catch (ValidationException ex)`.
- **`ErrorCodes.cs`**: insert the four new values directly after `TransportBoxDuplicateActiveBoxFound = 1405`
  (line 151), keeping the `// Transport module errors (14XX)` grouping comment as the section header
  — no new comment needed per value, matching the file's existing convention of comments only at
  module boundaries.
- **`i18n.ts`**: insert after the existing Transport block (after line 154's
  `TransportBoxDuplicateActiveBoxFound` entry) so all Transport strings stay contiguous.
- **Tests**: extend `TransportBoxStateTransitionTests.cs`/`TransportBoxCodeCaseHandlingTests.cs`
  assertions from `Should().Throw<ValidationException>()` to the more specific subclass only where
  a test's purpose is to pin the new type (not required everywhere — the plan's FR-1 acceptance
  criterion is that unmodified assertions keep passing, which they will either way). Add the two
  `ChangeTransportBoxStateHandlerTests` cases from plan FR-5.

## Risks and mitigations

- **Risk**: adding a specific catch in one handler but forgetting the parallel one-line addition to
  a sibling reachable handler, silently leaving that path uncovered. *Mitigation*: the FR-5-style
  test per handler (at minimum: one test per new catch clause added) forces the omission to fail
  loudly rather than silently regress to "Chyba validace" again.
- **Risk**: `Params` key names drift between the C# `Dictionary<string,string>` and the `{key}`
  tokens in the matching `i18n.ts` template (e.g. typo `boxCode` vs `code`). *Mitigation*: no
  compile-time link exists between the two — this is inherent to the existing pattern, not new
  risk introduced here (same exposure already exists for `TransportBoxNotFound`'s `{id}` etc.).
  Manual verification step (plan's step 5: drive the empty-box case, confirm real Czech text
  renders) is the correct guard; it is already in the plan and should not be skipped at review time.
- **Risk**: exact Czech wording is still a draft (flagged by both plan and design as a review-time
  decision). *Mitigation*: not a blocker — solo-dev + AI-review process per project facts; wording
  can be adjusted post-implementation without touching code shape.

## Prerequisites before implementation begins

None outstanding — investigation reachability claims, precedent patterns, and enum/test
mechanics are all confirmed against current source in this step. Implementation can proceed
directly from `design-01.md` with the two clarifications above (keep `CheckState` as a real type;
do not "complete" the catch set on `OpenOrResumeBoxByCodeHandler`).
