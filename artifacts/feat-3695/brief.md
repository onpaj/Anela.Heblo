# [arch-review] ExpeditionList: PrintExpeditionOrderHandler hardcodes DesiredStateId (26) in NonPrintableStates instead of reading from options

## Module
ExpeditionList

## Finding
`PrintExpeditionOrderHandler` declares a static dictionary of states that must not be re-printed:

```csharp
// backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/PrintExpeditionOrder/PrintExpeditionOrderHandler.cs lines 15-22
private static readonly IReadOnlyDictionary<int, string> NonPrintableStates = new Dictionary<int, string>
{
    { -3, "zrušeno/blokováno" },
    { 26, "Balí se" },          // ← hardcoded
    { 52, "Zabaleno" },
    { 70, "Předáno přepravci" },
};
```

State `26` is the "already in desired state" guard — it is also the default value of `PrintPickingListOptions.DesiredStateId` and the value the same handler reads from options when constructing the print request:

```csharp
// same handler, line 73
DesiredStateId = _options.Value.DesiredStateId,
```

The handler already injects `IOptions<PrintPickingListOptions>` (line 26) specifically to supply this value.

## Why it matters
`DesiredStateId` is configuration — it exists in `PrintPickingListOptions` precisely so it can be changed without code edits. If it is reconfigured to a different state ID:

- The print request correctly targets the new desired state (line 73 reads from options).
- But the guard at lines 57-66 still checks against the hardcoded `26` — orders already in the new desired state pass through and are double-printed; orders in state 26 are incorrectly blocked.

The three other entries in `NonPrintableStates` (-3, 52, 70) represent stable lifecycle states that don't move, so hardcoding those is fine. Only the `DesiredStateId` entry is volatile.

## Suggested fix
Remove the `26` entry from the static dictionary and add a runtime check against `_options.Value.DesiredStateId` before the dictionary lookup:

```csharp
// Replace the static dict (remove the 26 entry):
private static readonly IReadOnlyDictionary<int, string> NonPrintableStates = new Dictionary<int, string>
{
    { -3, "zrušeno/blokováno" },
    { 52, "Zabaleno" },
    { 70, "Předáno přepravci" },
};

// In Handle(), before the dictionary check (after fetching currentStatusId):
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
{ ... }
```

This keeps the guard correct regardless of how `DesiredStateId` is configured.

---
_Filed by daily arch-review routine on 2026-07-19._
