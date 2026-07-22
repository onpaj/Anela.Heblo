# Design: Fix hardcoded DesiredStateId in PrintExpeditionOrderHandler's non-printable state guard

## Component Design

### `PrintExpeditionOrderHandler` (`backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/PrintExpeditionOrder/PrintExpeditionOrderHandler.cs`)

Single MediatR handler for `PrintExpeditionOrderRequest`. Responsibility and dependencies are unchanged; only the internal state-guard logic changes.

**Current control flow (`Handle`):**
1. `IEshopOrderClient.GetOrderStatusIdAsync` resolves `currentStatusId`; a 404 short-circuits with `ErrorCodes.ShoptetOrderNotFound`.
2. `NonPrintableStates.TryGetValue(currentStatusId, ...)` — static dictionary lookup (`-3`, `26`, `52`, `70`) — a hit short-circuits with `ErrorCodes.ExpeditionOrderInvalidState`.
3. Builds `ExpeditionPickingRequest` with `DesiredStateId = _options.Value.DesiredStateId` and calls `IExpeditionListService.PrintPickingListAsync`.
4. `TotalCount == 0` → `ErrorCodes.ExpeditionOrderNotPrinted`; otherwise success.

**Change:** insert a new equality guard between steps 1 and 2, and remove the `26` entry from `NonPrintableStates`, so the "already in desired state" check and the print request's `DesiredStateId` both read from the single injected `IOptions<PrintPickingListOptions>` value — eliminating the possibility of the two drifting apart.

```csharp
// These are already-in-progress / done / cancelled states — printing them would double-print.
// Note: the "desired state after printing" (default 26) is NOT listed here — it is checked
// separately against _options.Value.DesiredStateId below, so it can never drift from the
// value used to build the print request.
private static readonly IReadOnlyDictionary<int, string> NonPrintableStates = new Dictionary<int, string>
{
    { -3, "zrušeno/blokováno" },
    { 52, "Zabaleno" },
    { 70, "Předáno přepravci" },
};
```

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

This block replaces the current lines 57–66 (the `NonPrintableStates.TryGetValue` block immediately following the 404 catch), placed immediately after the try/catch that resolves `currentStatusId`. No new fields, methods, or classes are introduced; the handler's constructor and its four existing dependencies (`IExpeditionListService`, `IEshopOrderClient`, `IOptions<PrintPickingListOptions>`, `ILogger<PrintExpeditionOrderHandler>`) are unchanged.

### `PrintPickingListOptions` (`backend/src/Anela.Heblo.Application/Features/ExpeditionList/PrintPickingListOptions.cs`)

No changes. `DesiredStateId` (`int`, default `26`) already exists, is already bound from configuration under `ConfigurationKey = "ExpeditionList"`, and is already injected into the handler via `IOptions<PrintPickingListOptions>`. It is now read at two call sites within the same method instead of one:
- New guard (added): `currentStatusId == _options.Value.DesiredStateId`.
- Existing print request (unchanged, line 72): `ExpeditionPickingRequest.DesiredStateId = _options.Value.DesiredStateId`.

## Data Schemas

No schema changes. `PrintExpeditionOrderRequest`, `PrintExpeditionOrderResponse`, and the `Params` dictionary shape (`orderCode`, `currentStatusName`) are unchanged, as is `ErrorCodes.ExpeditionOrderInvalidState` (`2103`). Behavior under default configuration (`DesiredStateId = 26`) is observably identical to today; under a non-default `DesiredStateId`, the guard now tracks the configured value instead of the stale literal `26`.
