# Development — TransportBox state-machine validation reasons collapse to generic ValidationError

## Summary

Implemented the fix as scoped in `plan-01.md`/`design-01.md`/`architecture-01.md`: reachable
`TransportBox` domain validation failures now get their own `ErrorCodes` and `Params`, replacing
the catch-all `ErrorCodes.ValidationError` + inert `Params["details"]` that the frontend template
never rendered. The generic `ValidationException` catch remains as a fallback for anything not
migrated (fail-safe, not silent).

## Files changed

**Domain**
- `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBoxExceptions.cs` (new) —
  four exception types subclassing `ValidationException`: `TransportBoxCodeRequiredException`,
  `TransportBoxCodeFormatException` (carries `EnteredCode`), `TransportBoxEmptyException` (carries
  `BoxCode`), `TransportBoxInvalidStateTransitionException` (carries `CurrentState` +
  `AllowedStates`).
- `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBox.cs` — five throw sites
  now throw the new typed exceptions instead of plain `ValidationException`, message text preserved
  verbatim: `Open()` empty/whitespace code (`:63`), `Open()` bad format (`:69`), `ToTransit()` no
  items (`:178`), `RevertToOpened()` wrong state (`:88-89`), `CheckState()` generic guard (`:252`).
  `RevertToOpened()`'s missing-code check (`:94`) and `Open()`'s wrong-state check (`:58`) were left
  as plain `ValidationException` per the design's dead-code/unreachable analysis.

**Application**
- `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` — four new `14XX` values:
  `TransportBoxCodeRequired = 1406` (BadRequest), `TransportBoxCodeInvalidFormat = 1407`
  (BadRequest), `TransportBoxEmpty = 1408` (UnprocessableEntity),
  `TransportBoxInvalidStateTransition = 1409` (UnprocessableEntity).
- `ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs` — added all 4 new `catch` clauses
  (most-specific-first), each populating `Params` with the keys the new i18n templates consume
  (`code`, or `currentState`/`allowedStates`), ahead of the unchanged generic
  `catch (ValidationException ex)` fallback.
- `OpenOrResumeBoxByCode/OpenOrResumeBoxByCodeHandler.cs` — added only the
  `TransportBoxCodeFormatException` catch (the only one reachable here; the code-required case is
  pre-empted by the handler's own whitespace pre-check).
- `RemoveItemFromBox/RemoveItemFromBoxHandler.cs`, `AddItemToBox/AddItemToBoxHandler.cs` — added
  only the `TransportBoxInvalidStateTransitionException` catch (reachable via `DecreaseItem`/
  `AddItem` → `CheckState(Opened, Opened)`), each with `currentState`/`allowedStates` Params.

**Frontend**
- `frontend/src/i18n.ts` — four new Czech translations under the Transport block, using
  `formatMessage`'s single-brace placeholder syntax (`{code}`, `{currentState}`,
  `{allowedStates}`).

**Tests**
- `TransportBoxStateTransitionTests.cs` — updated three existing assertions to the new specific
  exception subtypes (`TransportBoxEmptyException`, `TransportBoxCodeFormatException`); added
  `AssignBoxNumber_EmptyCode_ShouldThrowCodeRequired`,
  `CheckState_WrongState_ThrowsTransportBoxInvalidStateTransitionException`,
  `RevertToOpened_WrongState_ThrowsTransportBoxInvalidStateTransitionException`.
- `TransportBoxCodeCaseHandlingTests.cs` — updated the lowercase-invalid-format assertion to the
  new subtype.
- `ChangeTransportBoxStateHandlerTests.cs` — added
  `Handle_OpenedToInTransit_EmptyBox_ReturnsTransportBoxEmpty`,
  `Handle_NewToOpened_WhitespaceOnlyBoxCode_ReturnsTransportBoxCodeRequired`,
  `Handle_NewToOpened_MalformedBoxCode_ReturnsTransportBoxCodeInvalidFormat`.
  (`TransportBoxInvalidStateTransitionException` is not independently testable through this
  handler's normal flow — see Notes below — so its catch clause here is covered structurally, not
  by a dedicated handler test.)
- `RemoveItemFromBoxHandlerTests.cs`, `AddItemToBoxHandlerTests.cs` — added
  `Handle_BoxNotInOpenedState_ReturnsTransportBoxInvalidStateTransition` to each, asserting
  `ErrorCode == TransportBoxInvalidStateTransition` and `Params["currentState"]`/`["allowedStates"]`.
- `OpenOrResumeBoxByCodeHandlerTests.cs` — updated the existing
  `Handle_InvalidCodeFormat_ReturnsValidationError` test (renamed to
  `..._ReturnsTransportBoxCodeInvalidFormat`) since a malformed code now returns the specific code,
  not the generic fallback.

## Deviation from plan/design during implementation

FR-5 in `plan-01.md` asked for a `ChangeTransportBoxStateHandlerTests` case asserting
`TransportBoxInvalidStateTransition` from "a state-transition attempted from an unexpected current
state." Tracing the actual call graph showed this is **not reachable** through
`ChangeTransportBoxStateHandler`: `box.TransitionNode` is computed fresh from `box.State` on every
access (`_transitions[State]`), and nothing mutates `State` between transition selection and the
callback's execution within a single `Handle()` call — so the per-state node and the callback's own
internal state check are always consistent by construction. This matches the
architecture review's own characterization of that throw site as "edge case only (concurrent
modification / stale client state)."

Instead of writing a test that doesn't actually exercise the catch clause, I:
1. Kept the `TransportBoxInvalidStateTransitionException` catch in `ChangeTransportBoxStateHandler`
   (defense-in-depth, matches the design/architecture intent).
2. Added the FR-5-equivalent test where the exception **is** genuinely reachable —
   `RemoveItemFromBoxHandler` and `AddItemToBoxHandler`, which call `DecreaseItem`/`AddItem`
   directly without going through the transition-dictionary indirection, so a box not in `Opened`
   state legitimately hits `CheckState(Opened, Opened)` and throws.
3. Added two more directly-reachable `ChangeTransportBoxStateHandler` tests instead
   (`TransportBoxCodeRequired` via whitespace-only `BoxCode`, `TransportBoxCodeInvalidFormat` via
   malformed `BoxCode`) to keep coverage of that handler's other three new catch clauses, per the
   architecture review's stated mitigation: "one test per new catch clause added... forces the
   omission to fail loudly."

This is a test-design correction, not a scope or behavior change — the handler code still catches
all 4 exception types exactly as designed.

## How to verify

Backend:
```
export PATH="/Users/rem/.dotnet:$PATH"
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Transport|FullyQualifiedName~LocalizationCoverageTests|FullyQualifiedName~Architecture"
dotnet format Anela.Heblo.sln --verify-no-changes
```
Results: build 0 errors; 239 Transport/Localization tests passed; 34 Architecture tests passed
(module-boundary and Domain-can't-reference-Application checks unaffected); `dotnet format`
produced no diffs on the changed files.

Frontend:
```
cd frontend
npm install --legacy-peer-deps   # pre-existing react-i18next/typescript peer conflict, unrelated to this change
CI=true npm run build
npm run lint
```
Results: build compiled successfully. `npm run lint` reports 188 pre-existing problems (175
errors/13 warnings), all in unrelated `__tests__` files (testing-library rules) — confirmed
identical count via `git stash`/lint/`git stash pop` against the base branch, i.e. this change
introduces zero new lint issues. `i18n.ts` is not among the flagged files.

Manual verification of the fix's actual effect (empty-box dispatch): confirmed via
`Handle_OpenedToInTransit_EmptyBox_ReturnsTransportBoxEmpty` — the handler now returns
`ErrorCode = TransportBoxEmpty` with `Params["code"]`, and `i18n.ts` renders
`"Box {code} neobsahuje žádné položky — nelze jej odeslat prázdný"` with `{code}` substituted via
`formatMessage`'s single-brace regex — replacing the previous opaque "Chyba validace".
