# Design — TransportBox state-machine validation reasons collapse to generic ValidationError

No UI/UX section: this change adds no new screens, components, or interactions. The only
operator-visible effect is the *text* rendered by the existing global toast pipeline
(`frontend/src/api/client.ts` → `handleApiError` → `getErrorMessage`), which is unchanged in
structure — only the `errorCode`/`params` values flowing into it become more specific, and new
i18n string entries are added. A before/after example of that rendered text is included under
**Frontend i18n schema** since it's the observable outcome, but there is no component hierarchy
or wireframe to design.

## Component design

### 1. Domain — four new exception types

New file `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBoxExceptions.cs`
(one file, four small classes — mirrors the existing one-exception-per-concept convention seen in
`InvalidPhotoSearchPatternException`, adapted to subclass `ValidationException` so every existing
`catch (ValidationException)` and `Should().Throw<ValidationException>()` keeps working unchanged).

Each type is self-contained: it carries whatever data its handler-side `Params` need as typed
properties, computed from data the *domain* already has (`this.Code`, `this.State`, the allowed-
states list) — not from handler-local variables. This matters because in
`ChangeTransportBoxStateHandler.Handle`, `box` is declared with `var` **inside** the `try` block,
so it is out of scope in the `catch` clauses; carrying the data on the exception avoids needing to
hoist `box` to method scope just to build `Params`.

```csharp
public class TransportBoxCodeRequiredException : ValidationException
{
    public TransportBoxCodeRequiredException()
        : base("Box code cannot be null or empty") { }
}

public class TransportBoxCodeFormatException : ValidationException
{
    public string EnteredCode { get; }

    public TransportBoxCodeFormatException(string enteredCode)
        : base("Box code must follow format: B + 3 digits (e.g., B001, B123)")
    {
        EnteredCode = enteredCode;
    }
}

public class TransportBoxEmptyException : ValidationException
{
    public string? BoxCode { get; }

    public TransportBoxEmptyException(string? boxCode)
        : base("Cannot transition to InTransit state: Box must contain at least one item")
    {
        BoxCode = boxCode;
    }
}

public class TransportBoxInvalidStateTransitionException : ValidationException
{
    public TransportBoxState CurrentState { get; }
    public IReadOnlyList<TransportBoxState> AllowedStates { get; }

    public TransportBoxInvalidStateTransitionException(
        string message, TransportBoxState currentState, IReadOnlyList<TransportBoxState> allowedStates)
        : base(message)
    {
        CurrentState = currentState;
        AllowedStates = allowedStates;
    }
}
```

Message text is preserved **verbatim** from today's `ValidationException` messages (FR-1
acceptance criterion) — only the type changes, plus the two new data-carrying properties.

**Throw-site mapping in `TransportBox.cs`** (message text unchanged):

| Site | Old | New |
|---|---|---|
| `:63` `Open()` empty/whitespace code | `ValidationException("Box code cannot be null or empty")` | `throw new TransportBoxCodeRequiredException();` |
| `:69` `Open()` bad format | `ValidationException("Box code must follow format...")` | `throw new TransportBoxCodeFormatException(boxCode);` |
| `:178` `ToTransit()` no items | `ValidationException("Cannot transition to InTransit state: Box must contain at least one item")` | `throw new TransportBoxEmptyException(Code);` |
| `:88-89` `RevertToOpened()` wrong state | `ValidationException($"Cannot revert to Opened from {State}...")` | `throw new TransportBoxInvalidStateTransitionException(message, State, new[] { InTransit, Reserve, Quarantine });` |
| `:252` `CheckState()` generic guard | `ValidationException($"Unable to change state from {State} to {newState}...")` | `throw new TransportBoxInvalidStateTransitionException(message, State, allowedStates);` |

**Deliberate deviation from the plan's FR-1**: the plan proposed folding **both** `RevertToOpened`
throw sites (`:88-89` wrong-state *and* `:94` missing-code) into
`TransportBoxInvalidStateTransitionException`. Only `:88-89` fits that type's shape
(current-state + allowed-states). `:94` ("Cannot revert to Opened: Box code is required") is
structurally unreachable: a box can only be in `InTransit`/`Reserve`/`Quarantine` — the three
states `RevertToOpened` accepts — by having passed through `Open()`, which always sets `Code`
before that transition; nothing else nulls `Code` outside `Reset()` (which moves to `New`, not
one of the three). Giving this genuinely dead defensive check its own type/`ErrorCode`/i18n entry
would be new-abstraction-for-nothing. It stays a plain `ValidationException`, falling through to
the existing generic `ValidationError` catch-all (fails safe, not silent, per the plan's NFR).

`:58` (`Open()` wrong-state) and the two `ConfirmTransit()` sites (`:188`, `:193`) are unchanged —
confirmed unreachable from any handler in the investigation step; no new type needed.

### 2. Application — per-handler catch clauses (not identical across handlers)

Each of the four handlers only reaches a *subset* of the new exception types, because each calls a
different subset of `TransportBox` methods. Adding catches for exceptions a handler can't actually
throw would be dead code, so the catch list is handler-specific:

| Handler | Reachable new exception(s) | Reason |
|---|---|---|
| `ChangeTransportBoxStateHandler` | all 4 | drives the full state machine (`Open`, `ToTransit`, `RevertToOpened`, `CheckState`-guarded transitions) |
| `OpenOrResumeBoxByCodeHandler` | `TransportBoxCodeFormatException` only | calls `box.Open(code, ...)`; the code-required case is already pre-empted by its own `IsNullOrWhiteSpace(request.BoxCode)` check on the **untrimmed** input before `Open()` is reached, so `TransportBoxCodeRequiredException` cannot fire here — no catch added for it (would be dead code) |
| `RemoveItemFromBoxHandler` | `TransportBoxInvalidStateTransitionException` only | `DecreaseItem` → `CheckState(Opened, Opened)` |
| `AddItemToBoxHandler` | `TransportBoxInvalidStateTransitionException` only | `AddItem` → `CheckState(Opened, Opened)` |

In each handler, add the specific `catch` clauses **before** the existing `catch (ValidationException ex)`, which remains as the fallback for anything not migrated (per the plan's NFR — fail safe, not silent). Order (most-specific-first) mirrors the existing `GetGroupMembersHandler.cs` idiom of sequential typed catches.

`ChangeTransportBoxStateHandler.Handle` catch block (illustrative; other three handlers follow the
same shape with only their reachable subset):

```csharp
catch (TransportBoxCodeRequiredException ex)
{
    _logger.LogWarning("Box code required for box {BoxId}", request.BoxId);
    return new ChangeTransportBoxStateResponse
    {
        Success = false,
        ErrorCode = ErrorCodes.TransportBoxCodeRequired,
    };
}
catch (TransportBoxCodeFormatException ex)
{
    _logger.LogWarning("Invalid box code format for box {BoxId}: {Code}", request.BoxId, ex.EnteredCode);
    return new ChangeTransportBoxStateResponse
    {
        Success = false,
        ErrorCode = ErrorCodes.TransportBoxCodeInvalidFormat,
        Params = new Dictionary<string, string> { { "code", ex.EnteredCode } }
    };
}
catch (TransportBoxEmptyException ex)
{
    _logger.LogWarning("Attempted to dispatch empty box {BoxId}", request.BoxId);
    return new ChangeTransportBoxStateResponse
    {
        Success = false,
        ErrorCode = ErrorCodes.TransportBoxEmpty,
        Params = new Dictionary<string, string> { { "code", ex.BoxCode ?? "" } }
    };
}
catch (TransportBoxInvalidStateTransitionException ex)
{
    _logger.LogWarning("Invalid state transition for box {BoxId}: {Message}", request.BoxId, ex.Message);
    return new ChangeTransportBoxStateResponse
    {
        Success = false,
        ErrorCode = ErrorCodes.TransportBoxInvalidStateTransition,
        Params = new Dictionary<string, string>
        {
            { "currentState", ex.CurrentState.ToString() },
            { "allowedStates", string.Join(", ", ex.AllowedStates) }
        }
    };
}
catch (ValidationException ex)   // unchanged fallback for anything not migrated
{
    _logger.LogWarning("State transition validation failed for box {BoxId}: {Message}", request.BoxId, ex.Message);
    return new ChangeTransportBoxStateResponse
    {
        Success = false,
        ErrorCode = ErrorCodes.ValidationError,
        Params = new Dictionary<string, string> { { "details", ex.Message } }
    };
}
```

`RemoveItemFromBoxHandler` / `AddItemToBoxHandler` add only the
`TransportBoxInvalidStateTransitionException` catch (same shape as above, same two `Params` keys)
ahead of their existing generic `ValidationException` catch. `OpenOrResumeBoxByCodeHandler` adds
only the `TransportBoxCodeFormatException` catch ahead of its existing generic one.

## Data schemas

### `ErrorCodes` enum — new Transport module (`14XX`) values

Next free slot is `1406`. `[HttpStatusCode]` follows the existing convention: `BadRequest` for
malformed input, `UnprocessableEntity` for state-machine/business-state rejections (mirroring
`TransportBoxStateChangeError = 1402`, already `UnprocessableEntity`).

```csharp
// Transport module errors (14XX)
[HttpStatusCode(HttpStatusCode.NotFound)]
TransportBoxNotFound = 1401,
[HttpStatusCode(HttpStatusCode.UnprocessableEntity)]
TransportBoxStateChangeError = 1402,
[HttpStatusCode(HttpStatusCode.BadRequest)]
TransportBoxCreationError = 1403,
[HttpStatusCode(HttpStatusCode.BadRequest)]
TransportBoxItemError = 1404,
[HttpStatusCode(HttpStatusCode.Conflict)]
TransportBoxDuplicateActiveBoxFound = 1405,
[HttpStatusCode(HttpStatusCode.BadRequest)]
TransportBoxCodeRequired = 1406,
[HttpStatusCode(HttpStatusCode.BadRequest)]
TransportBoxCodeInvalidFormat = 1407,
[HttpStatusCode(HttpStatusCode.UnprocessableEntity)]
TransportBoxEmpty = 1408,
[HttpStatusCode(HttpStatusCode.UnprocessableEntity)]
TransportBoxInvalidStateTransition = 1409,
```

This enum is the source the OpenAPI-generated `frontend/src/api/generated/api-client.ts`
`ErrorCodes` string union is built from (per project fact: "OpenAPI TypeScript client is
auto-generated on build") — no manual frontend enum edit needed, only the i18n entries below.

### `ChangeTransportBoxStateResponse` / sibling response shapes — unchanged

`BaseResponse { Success, ErrorCode, Params: Dictionary<string,string> }` shape is untouched; only
the *values* differ for the migrated cases:

| ErrorCode | Params | Example |
|---|---|---|
| `TransportBoxCodeRequired` | *(none)* | — |
| `TransportBoxCodeInvalidFormat` | `code` | `{ "code": "ABCD" }` |
| `TransportBoxEmpty` | `code` | `{ "code": "B001" }` |
| `TransportBoxInvalidStateTransition` | `currentState`, `allowedStates` | `{ "currentState": "Closed", "allowedStates": "Opened" }` |

### Frontend i18n schema (`frontend/src/i18n.ts`, `errors` block)

New entries, single-brace placeholders only (per `formatMessage`'s regex —
`frontend/src/utils/errorHandler.ts:14-18` — matching the existing single-brace entries like
`TransportBoxDuplicateActiveBoxFound`, not the broken double-brace ones flagged as
pre-existing/out-of-scope in the plan):

```ts
// Transport module errors
TransportBoxCodeRequired: "Kód boxu je povinný",
TransportBoxCodeInvalidFormat:
  "Neplatný formát kódu boxu '{code}' — očekávaný formát B a 3 číslice (např. B001)",
TransportBoxEmpty: "Box {code} neobsahuje žádné položky — nelze jej odeslat prázdný",
TransportBoxInvalidStateTransition:
  "Box nelze v tomto stavu ({currentState}) takto změnit — povolené stavy: {allowedStates}",
```

(Wording is a draft proposal per the plan's open question; final copy is a review-time call for
the solo-dev + AI-review process, not a blocker to implementation.)

**Before/after observable effect** (the only "UI" surface this task touches):

- Before: operator dispatches an empty box → toast reads **"Chyba validace"** (fixed string, no
  reason, `Params.details` silently dropped).
- After: same action → toast reads **"Box B001 neobsahuje žádné položky — nelze jej odeslat
  prázdný"** (via `TransportBoxEmpty` + `code` param), rendered by the same unchanged toast
  pipeline.

## Dependencies and scope (confirmed from plan's open questions)

- **Scope across handlers**: all four handlers (`ChangeTransportBoxState`,
  `OpenOrResumeBoxByCode`, `RemoveItemFromBox`, `AddItemToBox`) are in scope, each catching only
  the exception types actually reachable through it (table above) — resolves the plan's open
  question in favor of the "fix all four" default, since three of them share the exact same root
  cause and leaving them collapsed would reintroduce the bug for add/remove-item and box-open
  flows.
- **`:94` `RevertToOpened` missing-code throw**: left on generic `ValidationException` (see
  deviation note above) — resolves the plan's "RevertToOpened's two throw sites" open question by
  splitting them instead of folding both, since only one fits the new type's shape.
- **`ConfirmTransit` dead code**: confirmed out of scope, no change — matches plan.
- **Exact Czech wording**: proposed above as a starting draft, not finalized — matches plan's
  note that this is a review-time copy decision.
