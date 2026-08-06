# Plan — TransportBox state-machine validation reasons collapse to generic ValidationError

## Summary

`ChangeTransportBoxStateHandler` (and, as this investigation found, three sibling handlers in the same module) catches every `TransportBox` domain `ValidationException` and maps it to a single generic `ErrorCodes.ValidationError`, passing the real reason only in a `Params["details"]` value the frontend template never substitutes. Operators lose the specific, actionable reason (empty box, bad box code, invalid state transition) and see only "Chyba validace". The fix is to give the reachable, operator-facing failure modes their own `ErrorCodes` (with `Params` the frontend actually consumes), following a pattern already used elsewhere in this codebase for exactly this problem.

## Context

- `docs/architecture/localization.md` §"Error Code Mapping" requires one distinct, localizable `ErrorCode` per failure, verified by `LocalizationCoverageTests.FrontendI18n_ShouldHaveTranslationsForAllErrorCodes`. That test only checks a translation key exists per enum value — it does not catch a generic catch-all being used for many distinct causes, which is how this gap survived.
- `Anela.Heblo.Domain` has **no project reference to `Anela.Heblo.Application`** (verified via `.csproj`), so domain code cannot throw or reference the `ErrorCodes` enum directly. Any fix must keep the mapping from domain failure → `ErrorCodes` in the Application-layer handler(s), not in `TransportBox.cs`.
- Precedent for the fix already exists in this codebase: `GetGroupMembersHandler.cs:34-77` and `BackfillArticleRequestedByHandler.cs:42-52` catch several distinct custom exception types in sequence and map each to a different `ErrorCodes`. Domain-specific exception types with extra data already exist too (`InvalidPhotoSearchPatternException` has a `Pattern` property, `IssuedInvoiceClientException` has `RawAdapterResponse`). The established idiom is: **one small named exception type per distinct domain failure, caught by type in the handler** — not a single exception carrying an embedded reason code (Domain can't reference `ErrorCodes`, and no such "tagged exception" pattern exists anywhere else in the repo, so introducing one here would be a new, unprecedented shape).
- The same collapse-to-`ValidationError` pattern was found in three more handlers in this module, all catching the same shared `TransportBox` domain exceptions: `OpenOrResumeBoxByCodeHandler.cs:90`, `RemoveItemFromBoxHandler.cs:105`, `AddItemToBoxHandler.cs:114`. This wasn't mentioned in the original finding but is the identical root cause — see Open Questions for the scoping call.

## Investigation notes (reachability check)

`TransportBox.cs` has 9 `throw new ValidationException(...)` sites. Tracing each against the actual state-transition wiring (`_transitions` static dictionary, `TransportBoxTransition` callbacks) and the handler's call graph shows they are **not all equally reachable** from the API:

| Site | Message | Reachable via API today? |
|---|---|---|
| `:58` Open() — wrong state to assign code | "Box number can only be assigned..." | Practically no — `HandleNewToOpened` only calls this transition from `New`, and the transition's own `condition` already gates it |
| `:63` Open() — empty/whitespace code | "Box code cannot be null or empty" | **Yes** — `HandleNewToOpened` pre-checks `IsNullOrEmpty`, but whitespace-only input (`"  "`) passes that check and still reaches this domain exception |
| `:69` Open() — bad format | "Box code must follow format..." | **Yes** — no pre-check for format exists in the handler |
| `:88-89` RevertToOpened() — wrong state | "Cannot revert to Opened from {State}..." | Edge case only (concurrent modification) |
| `:94` RevertToOpened() — missing code | "Cannot revert to Opened: Box code is required" | Edge case only, same as above |
| `:178` ToTransit() — no items | "Box must contain at least one item" | **Yes — this is the concrete "dispatch an empty box" scenario in the finding** |
| `:188` ConfirmTransit() — no confirmation | "Box number confirmation is required" | **No** — see below |
| `:193` ConfirmTransit() — code mismatch | "Box number mismatch: entered '{x}' but expected '{y}'" | **No** — see below |
| `:252` CheckState() — generic guard | "Unable to change state from {State} to {newState}..." | Edge case only (concurrent modification / stale client state) |

**Important finding-correction:** `ConfirmTransit(string confirmedBoxNumber, ...)` — the method that produces the "box number mismatch" message quoted in the finding — is **never called from any Application handler**. Only `TransportBox.ToTransit()` is wired into the `Opened → InTransit` transition, and it takes no confirmation parameter. `ConfirmTransit` is exercised only by a domain unit test (`TransportBoxCodeCaseHandlingTests.cs:55-93`). So today, an operator cannot actually hit the "box number mismatch" message through the receive/dispatch flow — the finding's illustrative example for that specific message doesn't fire. The *general* problem (opaque "Chyba validace" for a real, reachable domain rejection) is still correct and is well demonstrated by the empty-box (`ToTransit`) and bad-format/whitespace (`Open`) cases, which **are** reachable.

## Functional requirements

**FR-1 — Domain: distinguishable exception types for reachable failures**
Replace the generic `ValidationException` at the reachable throw sites with small, named exception types (subclassing `ValidationException` so any `catch (ValidationException)` fallback and existing `Should().Throw<ValidationException>()` domain test assertions keep working unchanged):
- `TransportBoxCodeRequiredException` — `TransportBox.cs:63`
- `TransportBoxCodeFormatException` — `TransportBox.cs:69`
- `TransportBoxEmptyException` — `TransportBox.cs:178`
- `TransportBoxInvalidStateTransitionException` — `TransportBox.cs:252` (generic `CheckState` guard); fold the two `RevertToOpened` sites (`:88-89`, `:94`) into this same type too, since they're rare defensive edge cases, not everyday operator scenarios (keeps the new-type surface small per "surgical changes").
- Acceptance: existing domain tests that assert `Should().Throw<ValidationException>()` for these call sites still pass without modification; message text is preserved verbatim (some handler logging depends on `ex.Message`).

**FR-2 — Application: distinct ErrorCodes + Params per exception type**
Add new `ErrorCodes` values in the Transport module's `14XX` range (next free: `1406`+) — e.g. `TransportBoxCodeRequired`, `TransportBoxCodeInvalidFormat`, `TransportBoxEmpty`, `TransportBoxInvalidStateTransition`. In `ChangeTransportBoxStateHandler.Handle`, add `catch` clauses for each new exception type **before** the existing `catch (ValidationException ex)` fallback (kept as a safety net for anything not yet migrated), each returning the matching `ErrorCode` and a `Params` dictionary containing exactly the keys the new frontend templates will consume (e.g. `{ "state", box.State.ToString() }` for the invalid-transition case — no more inert `"details"` key).
- Acceptance: a request that triggers `ToTransit()` on an empty box returns `ErrorCode = TransportBoxEmpty` (not `ValidationError`); a request with a whitespace-only or malformed box code returns `TransportBoxCodeRequired` / `TransportBoxCodeInvalidFormat` respectively.

**FR-3 — Frontend: real translations for the new codes**
Add Czech translations to `frontend/src/i18n.ts`'s `errors` block for each new `ErrorCodes` value, phrased for the operator (not the raw domain exception text), using **single-brace** placeholders (`{state}`, etc.) — `formatMessage` (`frontend/src/utils/errorHandler.ts:14-18`) only substitutes `\{key\}`, not the `{{key}}` form used inconsistently by a few other existing entries (that's a separate pre-existing bug, see Dependencies/Scope).
- Acceptance: `getErrorMessage(ErrorCodes.TransportBoxEmpty)` etc. renders full operator-readable Czech text with any params substituted, not the raw enum name or an empty placeholder.

**FR-4 — Coverage gate stays green**
`LocalizationCoverageTests.FrontendI18n_ShouldHaveTranslationsForAllErrorCodes` and `ErrorCodes_ShouldFollowNewModulePrefixFormat` must pass with the new enum values (they will, once i18n.ts has the string-keyed entries — no changes needed to the test itself).

**FR-5 — Test coverage for the previously-untested catch path**
`ChangeTransportBoxStateHandlerTests.cs` currently has zero tests exercising the `ValidationException` catch block at all. Add cases for at least:
- Dispatching a box with no items → asserts `ErrorCode == TransportBoxEmpty`.
- A state-transition attempted from an unexpected current state → asserts `ErrorCode == TransportBoxInvalidStateTransition` and `Params` contains the expected key(s).

## Non-functional requirements

- No change to actual business rules or the state machine's allowed transitions — this is purely an error-reporting/localization fix; validation still rejects the same cases it does today.
- Backward compatibility: `ChangeTransportBoxStateResponse` shape (`BaseResponse` with `Success`/`ErrorCode`/`Params`) is unchanged; only the *value* of `ErrorCode` and the *content* of `Params` change for the migrated cases. No frontend caller currently branches on `ErrorCode === ValidationError` for these flows (verified: the mutation hook `useTransportBoxes.ts:151-203` relies entirely on the global toast handler, not local error-code branching), so this is a safe, additive change.
- Keep the generic `ValidationError` fallback in place for any `ValidationException` not covered by the new named types, so a future, not-yet-migrated domain validation doesn't silently break (fails safe, not silent).

## Data model

No persistent data model changes. Conceptual additions only:
- 4 new `Anela.Heblo.Domain.Features.Logistics.Transport` exception types (subclassing `System.ComponentModel.DataAnnotations.ValidationException`).
- 4 new `Anela.Heblo.Application.Shared.ErrorCodes` enum values (`14XX` range) with `[HttpStatusCode(...)]` attributes matching existing convention (`BadRequest` for input-shape errors like code format/required; `UnprocessableEntity` for state-machine rejections, mirroring `TransportBoxStateChangeError`'s existing `422`).

## Interfaces

- No new endpoints, no request/response contract changes. `POST` transport-box state-change endpoint (backed by `ChangeTransportBoxStateRequest`/`Response`) returns the same JSON shape, with more specific `errorCode`/`params` values for the migrated cases.
- No frontend component changes required for the primary flow: the existing global toast pipeline (`frontend/src/api/client.ts:221-362` → `handleApiError`/`getErrorMessage` in `errorHandler.ts`) already renders whatever `errorCode`+`params` the backend sends, driven purely by `i18n.ts` entries. Adding translations is sufficient to surface the new codes; no code change needed in the toast plumbing itself.

## Dependencies and scope

**In scope:**
- `TransportBox.cs` (new exception types at the 4 reachable-and-relevant throw sites).
- `ChangeTransportBoxStateHandler.cs` (new catch clauses + Params).
- `ErrorCodes.cs` (new enum values).
- `frontend/src/i18n.ts` (new translations).
- Domain and handler test updates.

**Out of scope (flagged, not blocking):**
- Wiring `ConfirmTransit`/a box-number-confirmation step into an actual UI flow — it's currently dead code with zero Application-layer callers; making it reachable is a separate, larger feature decision, not a localization fix.
- The pre-existing `{{param}}` vs `{param}` placeholder-syntax inconsistency elsewhere in `i18n.ts` (e.g. `AbraIntegrationFailed`, `ShoptetSyncFailed`, `StockTakingFailed`, `SupplierLookupFailed` all use double braces, which `formatMessage`'s regex does not fully strip) — a separate latent bug, unrelated to this module.
- Whether to also fix the same collapse pattern in `OpenOrResumeBoxByCodeHandler.cs:90`, `RemoveItemFromBoxHandler.cs:105`, `AddItemToBoxHandler.cs:114` — see Open Questions.

## Rough plan

1. **Domain**: add the 4 new exception types under `Anela.Heblo.Domain/Features/Logistics/Transport/`; replace the corresponding `throw new ValidationException(...)` calls in `TransportBox.cs` with `throw new <NewType>(...)`, preserving exact message text.
2. **Application**: add the 4 new `ErrorCodes` values (next available in `14XX`); update `ChangeTransportBoxStateHandler.Handle`'s try/catch to add the new specific catches (most-specific first, generic `ValidationException` last as fallback), each populating `Params` with the exact key(s) the new i18n templates need.
3. **Frontend**: add the 4 new translation entries to `i18n.ts`'s `errors` block with single-brace placeholders; word them for an operator scanning barcodes, not as raw exception text.
4. **Tests**: update/extend `TransportBox` domain tests to assert the new exception types where it matters for handler disambiguation; add the two new `ChangeTransportBoxStateHandlerTests` cases from FR-5; run `LocalizationCoverageTests`.
5. **Validate**: `dotnet build` + `dotnet format` (backend), `npm run build` + `npm run lint` (frontend), run the affected test suites; manually drive the empty-box dispatch case (or an equivalent test) to confirm the Czech message is now specific instead of "Chyba validace".
6. **Decide and apply** the sibling-handler scope call (see Open Questions) before closing out, so the fix doesn't leave 3 of 4 handlers with the same bug.

## Open questions

- **Scope across handlers**: Should this fix also cover `OpenOrResumeBoxByCodeHandler`, `RemoveItemFromBoxHandler`, `AddItemToBoxHandler` — which catch the same shared `TransportBox` domain exceptions and collapse them the same way — or strictly the `ChangeTransportBoxStateHandler` named in the finding? Default assumed: fix all four in one pass, since they share the same domain exception types and leaving three unfixed reintroduces the identical bug for `AddItem`/`RemoveItem`/box-open flows. Flag for confirmation before the architecture/dev step commits to scope.
- **Dead `ConfirmTransit` code**: leave unreached (out of scope) per above, or is there a hidden caller (e.g. reflection-based dispatch, a UI action not surfaced by static search) that should be checked before assuming it's dead? Default assumed: it's genuinely unreferenced outside its own domain test; no action taken.
- **RevertToOpened's two throw sites**: folded into the generic `TransportBoxInvalidStateTransitionException` bucket rather than given their own codes, since they're rare defensive paths (concurrent edits) rather than everyday operator-facing scenarios. If the business wants a distinct "someone else already moved this box" message, that would need its own code — default assumed not needed for this fix.
- **Exact Czech wording** for the four new translations is a copy decision for whoever reviews (solo dev + AI review per project facts) — draft wording should be proposed at the design/dev step, not finalized here.
