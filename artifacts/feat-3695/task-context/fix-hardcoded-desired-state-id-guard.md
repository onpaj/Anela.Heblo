### task: fix-hardcoded-desired-state-id-guard

## Goal
In `PrintExpeditionOrderHandler.Handle`, replace the hardcoded `26` entry in the static `NonPrintableStates` dictionary with a runtime check against `_options.Value.DesiredStateId`, so the "order already in desired state" guard can never drift from the `DesiredStateId` actually used when building the print request (`ExpeditionPickingRequest.DesiredStateId` at line 72). This closes an architecture-review finding (see `artifacts/feat-3695/arch-review.r1.md`) with a known, unambiguous fix; no product behavior changes under default configuration.

## Files to change

1. `backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/PrintExpeditionOrder/PrintExpeditionOrderHandler.cs`
2. `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/PrintExpeditionOrderHandlerTests.cs`

## Implementation notes

### 1. `PrintExpeditionOrderHandler.cs`

**Remove** the `{ 26, "Balí se" }` entry (current line 18) from the static `NonPrintableStates` dictionary, leaving only the three genuinely stable, non-configurable lifecycle states:

```csharp
private static readonly IReadOnlyDictionary<int, string> NonPrintableStates = new Dictionary<int, string>
{
    { -3, "zrušeno/blokováno" },
    { 52, "Zabaleno" },
    { 70, "Předáno přepravci" },
};
```

(Optionally add a short comment noting that the "desired state after printing" is checked separately below against `_options.Value.DesiredStateId`, per the design doc — this is a nice-to-have, not required.)

**Insert** a new equality guard immediately after the try/catch that resolves `currentStatusId` (current lines 44–55) and immediately **before** the existing `NonPrintableStates.TryGetValue` block (current lines 57–66). Order matters — the new check must run first:

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

Everything else in the method (the 404 handling above, the `ExpeditionPickingRequest` construction and `PrintPickingListAsync` call below, the `TotalCount == 0` check, the final success response) is unchanged — do not touch it.

Do not change the constructor, dependencies (`IExpeditionListService`, `IEshopOrderClient`, `IOptions<PrintPickingListOptions>`, `ILogger<PrintExpeditionOrderHandler>`), `ErrorCodes`, or the `Params` dictionary shape (`orderCode`, `currentStatusName`).

### 2. `PrintPickingListOptions.cs` — no change needed
`DesiredStateId` (default `26`) already exists and is already injected via `IOptions<PrintPickingListOptions>`. Do not modify this file.

## Testing

File: `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/PrintExpeditionOrderHandlerTests.cs`

- **Do not modify** `Handle_OrderInNonPrintableState_ReturnsInvalidStateError` (the `[Theory]` with `[InlineData(-3, ...)]`, `[InlineData(26, "Balí se")]`, `[InlineData(52, ...)]`, `[InlineData(70, ...)]`). `CreateHandler()` builds the handler with `Options.Create(new PrintPickingListOptions())`, i.e. default `DesiredStateId = 26`, so the `26` case must still pass unmodified — it now hits the new equality branch instead of the dictionary. Run it explicitly after the change and confirm it's green; treat any failure as a regression signal, not something to fix by editing the test.
- **Do not modify** `Handle_ValidState_PrintsWithOrderCodeAndDesiredState26` or the other existing tests (`Handle_NothingPrinted_ReturnsNotPrintedError`, `Handle_OrderLookupReturns404_ReturnsNotFoundError`, `Handle_OrderLookupReturns500_PropagatesException`) — all use status `-2` or unrelated paths, unaffected by this change.
- **Add** a new test that exercises a non-default `DesiredStateId`, constructing the handler directly (not via the shared `CreateHandler()` helper, since that hardcodes default options) with `Options.Create(new PrintPickingListOptions { DesiredStateId = 99 })`, and asserts both halves of the fix:
  - Order with status `99` → `result.Success` is `false`, `result.ErrorCode == ErrorCodes.ExpeditionOrderInvalidState`, `result.Params!["currentStatusName"] == "Balí se"`, and `_service.PrintPickingListAsync` is never called (`Times.Never`).
  - Order with status `26` (the old hardcoded value, now no longer special-cased) → the handler proceeds to call `_service.PrintPickingListAsync` (assert `Times.Once`, not just `result.Success`), proving the stale `26` entry no longer blocks printing when `DesiredStateId` has been reconfigured. Set up `_service` to return a non-zero `TotalCount` so the call also results in `result.Success == true`.

  This test is what actually proves the bug (drift between the guard and the configured `DesiredStateId`) is fixed — do not skip it.

Run the full `PrintExpeditionOrderHandlerTests` suite (and the broader `dotnet test` for the touched project) to confirm nothing else regressed.

## Acceptance criteria

- `NonPrintableStates` no longer contains a `26` entry; only `-3`, `52`, `70` remain.
- A new equality check `currentStatusId == _options.Value.DesiredStateId` runs before the `NonPrintableStates` lookup and returns `ErrorCodes.ExpeditionOrderInvalidState` with `Params["currentStatusName"] = "Balí se"` when it matches.
- Under default configuration (`DesiredStateId = 26`, the common case), handler behavior is byte-for-byte unchanged from today: status `26` is rejected with `ExpeditionOrderInvalidState` / `"Balí se"`; statuses `-3`, `52`, `70` are rejected via the dictionary with their existing names; all other statuses proceed to print.
- Under a non-default configuration (e.g. `DesiredStateId = 99`), status `99` is now correctly rejected as invalid-state, and status `26` is no longer incorrectly blocked — it proceeds to print like any other non-listed state.
- No change to `PrintExpeditionOrderRequest`/`PrintExpeditionOrderResponse` contracts, `ErrorCodes` values, `Params` dictionary keys, or the ordering/short-circuiting of the Shoptet-404 check (still runs first).
- All existing tests in `PrintExpeditionOrderHandlerTests.cs` pass unmodified; the new non-default-`DesiredStateId` test passes and specifically asserts the previously-broken scenario (status `26` proceeding to print, via `Times.Once` verification, not just `result.Success`).
- `dotnet build` and `dotnet format` succeed for the backend; `dotnet test` passes for the touched test project.
