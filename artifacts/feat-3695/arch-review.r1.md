# Architecture Review: Fix hardcoded DesiredStateId in PrintExpeditionOrderHandler's non-printable state guard

## Skip Design: true

Confirmed against the actual change: this touches a single MediatR handler (`PrintExpeditionOrderHandler.Handle`) in the backend Application layer only. No controller route, DTO shape, `ErrorCodes` value, or response contract changes — `PrintExpeditionOrderResponse`, `Params` keys, and HTTP status mapping are all unchanged. There is no frontend involved: the React client already handles `ExpeditionOrderInvalidState` generically via `currentStatusName`, and that continues to work identically. No new or changed UI component, screen, or visual decision exists here.

## Architectural Fit Assessment

This is a textbook "configuration drift" bug, fully contained inside one Vertical Slice use case (`Features/ExpeditionList/UseCases/PrintExpeditionOrder`). It aligns with existing patterns exactly:

- The handler already depends on `IOptions<PrintPickingListOptions>` (constructor-injected, standard .NET Options pattern) and already reads `_options.Value.DesiredStateId` at line 72 to build `ExpeditionPickingRequest.DesiredStateId`. The fix does not introduce a new dependency, new DI registration, or a new configuration key — it makes an *existing* injected value do double duty where a literal was incorrectly used instead.
- `PrintPickingListOptions` (`backend/src/Anela.Heblo.Application/Features/ExpeditionList/PrintPickingListOptions.cs`) is a plain options class bound to `ConfigurationKey = "ExpeditionList"`, consistent with the project's Options pattern conventions elsewhere in the codebase.
- The three remaining `NonPrintableStates` entries (`-3`, `52`, `70`) are correctly left as compile-time constants — they represent Shoptet lifecycle states unrelated to this module's own configurable "desired state," so no further configurability is warranted (confirmed against the brief and spec; out of scope per spec's "Out of Scope" section).
- Error handling continues to use the existing `ErrorCodes.ExpeditionOrderInvalidState` (value `2103`) and the existing `BaseResponse`/`Params` dictionary shape (`Anela.Heblo.Application/Shared/ErrorCodes.cs`, `BaseResponse.cs`) — no new error code needed, no contract change.

Single integration point: `PrintExpeditionOrderHandler.Handle`. No other file in the repo references `NonPrintableStates` (verified via repo-wide grep) — the fix is fully local.

## Proposed Architecture

### Component Overview

```
PrintExpeditionOrderRequest
        │
        ▼
PrintExpeditionOrderHandler.Handle
        │
        ├─ 1. IEshopOrderClient.GetOrderStatusIdAsync  → currentStatusId
        │        └─ 404 → ErrorCodes.ShoptetOrderNotFound   (unchanged)
        │
        ├─ 2. NEW: currentStatusId == _options.Value.DesiredStateId ?
        │        └─ yes → ErrorCodes.ExpeditionOrderInvalidState
        │                  Params["currentStatusName"] = "Balí se"   (unchanged literal)
        │
        ├─ 3. NonPrintableStates.TryGetValue(currentStatusId)   (dictionary now has 3 entries: -3, 52, 70)
        │        └─ hit → ErrorCodes.ExpeditionOrderInvalidState (unchanged)
        │
        ├─ 4. IExpeditionListService.PrintPickingListAsync(DesiredStateId = _options.Value.DesiredStateId)  (unchanged)
        │        └─ TotalCount == 0 → ErrorCodes.ExpeditionOrderNotPrinted  (unchanged)
        │
        └─ 5. success → PrintExpeditionOrderResponse()  (unchanged)

Config source: IOptions<PrintPickingListOptions>  (ConfigurationKey = "ExpeditionList")
  used at BOTH step 2 (new) and step 4 (existing) — single source of truth restored.
```

No new components, no new services, no new DI registrations. The only structural change is inserting one equality check between existing steps 1 and 3.

### Key Design Decisions

#### Decision 1: Where the desired-state check lives relative to the dictionary lookup

**Options considered:**
1. Keep `26` in the dictionary but also add `_options.Value.DesiredStateId` to it dynamically at handler construction time (mutate/rebuild a per-instance dictionary).
2. Add a standalone equality check against `_options.Value.DesiredStateId` **before** the `NonPrintableStates` lookup, as the brief and spec prescribe, and remove `26` from the static dictionary.
3. Merge everything into a single dynamic per-request dictionary built from `_options.Value.DesiredStateId` plus the three static entries.

**Chosen approach:** Option 2 — exactly as specified in the brief and spec. Add:
```csharp
if (currentStatusId == _options.Value.DesiredStateId)
{
    return new PrintExpeditionOrderResponse(
        ErrorCodes.ExpeditionOrderInvalidState,
        new Dictionary<string, string>
        {
            { "orderCode", request.OrderCode },
            { "currentStatusName", "Balí se" },
        });
}

if (NonPrintableStates.TryGetValue(currentStatusId, out var stateName))
{
    return new PrintExpeditionOrderResponse(
        ErrorCodes.ExpeditionOrderInvalidState,
        new Dictionary<string, string>
        {
            { "orderCode", request.OrderCode },
            { "currentStatusName", stateName },
        });
}
```
placed immediately after the try/catch that resolves `currentStatusId` (i.e., replacing the current lines 57–66), and remove the `{ 26, "Balí se" }` entry from the static `NonPrintableStates` dictionary (line 18).

**Rationale:**
- Options 1 and 3 turn `NonPrintableStates` from a `static readonly` compile-time constant into per-instance/per-request mutable state, which is unnecessary complexity for a value that's looked up at most twice per request and adds allocation on every `Handle` call (rebuilding a dictionary) for no behavioral benefit over a single `==` check.
- Option 2 is the minimal diff, keeps `NonPrintableStates` genuinely static (it only contains states that never change), and makes the two configuration-driven checks in this handler (guard check, print request) read from the exact same `_options.Value.DesiredStateId` — eliminating the possibility of drift entirely, not just today's specific instance of it.
- Placing the check **before** the dictionary lookup (not after, not merged) preserves a clean, obviously-correct precedence and matches exactly what the brief's suggested fix and the spec's FR-1 acceptance criteria describe — no reason to deviate.

## Implementation Guidance

### Directory / Module Structure

No new files. Single file edit:
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/PrintExpeditionOrder/PrintExpeditionOrderHandler.cs`
  - Remove line 18 (`{ 26, "Balí se" },`) from the `NonPrintableStates` initializer.
  - Insert the new `if (currentStatusId == _options.Value.DesiredStateId)` guard between the current try/catch block (ends line 55) and the existing `NonPrintableStates.TryGetValue` check (currently lines 57–66).

### Interfaces and Contracts

No interface or contract changes:
- `PrintExpeditionOrderRequest`, `PrintExpeditionOrderResponse`, `ErrorCodes.ExpeditionOrderInvalidState` (2103), and the `Params` dictionary shape (`orderCode`, `currentStatusName`) are all unchanged and must stay unchanged — this is a pure internal logic fix.
- `PrintPickingListOptions.DesiredStateId` (default `26`) is consumed, not modified. No new options field.
- `IExpeditionListService`, `IEshopOrderClient` — no changes.

### Data Flow

Identical to today under default configuration (`DesiredStateId = 26`): an order in status 26 hits the new equality check first and returns `ExpeditionOrderInvalidState` with `currentStatusName = "Balí se"` — same observable outcome as before, just reached one branch earlier.

Under non-default configuration (e.g. `DesiredStateId = 99`), the flow now diverges correctly from today's buggy behavior:
- Status `99` → blocked by the new equality check (today: would have printed, over-printing risk — **now fixed**).
- Status `26` → no longer in the static dictionary, falls through the equality check (99 ≠ 26) and the dictionary lookup (no longer contains 26), proceeds to print normally, exactly like any other non-terminal state (today: incorrectly blocked forever — **now fixed**).

### Test Updates Required

`backend/test/Anela.Heblo.Tests/Features/ExpeditionList/PrintExpeditionOrderHandlerTests.cs`:

- `Handle_OrderInNonPrintableState_ReturnsInvalidStateError` (lines 25–42): the `[InlineData(26, "Balí se")]` case must continue to pass **unmodified** — confirm after the fix that it still does, since `CreateHandler()` (line 22) constructs `Options.Create(new PrintPickingListOptions())`, i.e. default `DesiredStateId = 26`, so status `26` still resolves to `ExpeditionOrderInvalidState` / `"Balí se"`, now via the new equality branch instead of the dictionary. No source change needed to this test per spec's dependency note, but it is the test that proves the fix didn't regress default behavior — run it explicitly.
- `Handle_ValidState_PrintsWithOrderCodeAndDesiredState26` (lines 44–63): uses status `-2`, unaffected, should continue to pass unmodified.
- **New test recommended** (spec calls this out as "recommended... to lock in the fix," not a hard requirement, but this review treats it as necessary for a correctness-bearing bugfix — do not skip it): a test that constructs the handler with `Options.Create(new PrintPickingListOptions { DesiredStateId = 99 })` and asserts:
  - status `99` → `ExpeditionOrderInvalidState`.
  - status `26` → proceeds to call `PrintPickingListAsync` (i.e., no longer blocked) — this is the case that actually exercises the bug this fix closes, so it must assert `_service.Verify(... Times.Once)` or equivalent, not just `result.Success`.
- No changes needed to `PrintExpeditionOrderRequestValidatorTests.cs` — validator is unrelated to this state guard.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Regression on default-config behavior (status 26 no longer blocked) | Low | Existing theory test case `[InlineData(26, "Balí se")]` already covers this; must pass unmodified after the fix — treat any failure as a blocking signal, not something to "fix" by editing the test. |
| New equality check placed after the dictionary lookup instead of before, causing subtle precedence bugs if `DesiredStateId` is ever reconfigured to overlap with -3/52/70 | Low | Spec and brief are explicit about ordering (check before dictionary); this review reaffirms it. Overlap is currently impossible since -3/52/70 are fixed and DesiredStateId defaults to 26, but the earlier-check ordering is what makes the guard correct even if that changes. |
| Forgetting to remove `26` from the static dictionary while adding the new check (leaving both) | Low | Harmless functionally under default config (redundant match, same outcome) but reintroduces the exact drift risk this fix exists to remove if `DesiredStateId` is later changed. Code review / PR checklist should explicitly verify the `26` entry is gone. |
| No test asserts the previously-broken non-default-config scenario | Medium | Add the recommended new unit test (see Test Updates Required) covering `DesiredStateId = 99`; without it, this fix could silently regress again with no test signal. |

## Specification Amendments

None. The spec (`artifacts/feat-3695/spec.r1.md`) is already precise, unambiguous, and matches the actual code at the cited line numbers (verified: lines 15–22 static dictionary, line 26 `IOptions<PrintPickingListOptions>` injection, line 72 `DesiredStateId = _options.Value.DesiredStateId`). One clarification worth surfacing to the implementer rather than a spec change: the spec's "recommended" new test for non-default `DesiredStateId` should be treated as effectively required by this review (see Risks table) — a correctness fix with no regression test for the exact scenario it fixes is incomplete.

## Prerequisites

None. No migrations, no new configuration keys, no infrastructure changes. `PrintPickingListOptions.DesiredStateId` already exists, is already bound from configuration (`ConfigurationKey = "ExpeditionList"`), and is already injected into the handler. Implementation can start immediately.
