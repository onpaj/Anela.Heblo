## Module
ExpeditionList

## Finding
`PrintExpeditionOrderHandler` checks whether the current order is already in the desired state and returns an error with a human-readable status name. The state ID comes from configuration (`_options.Value.DesiredStateId`), but the name is hardcoded as the string literal `"Balí se"`:

**`backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/PrintExpeditionOrder/PrintExpeditionOrderHandler.cs:57-65`**
```csharp
if (currentStatusId == _options.Value.DesiredStateId)
{
    return new PrintExpeditionOrderResponse(
        ErrorCodes.ExpeditionOrderInvalidState,
        new Dictionary<string, string>
        {
            { "orderCode", request.OrderCode },
            { "currentStatusName", "Balí se" },   // ← hardcoded
        });
}
```

The other non-printable states are handled correctly via the `NonPrintableStates` dictionary (lines 16-21), which maps IDs to names. The desired-state case is not included in that dictionary, so its name is only expressed as a hardcoded literal tied to the default value of 26.

## Why it matters
`DesiredStateId` is configurable (it defaults to 26 in `PrintPickingListOptions` but is overridable in Key Vault/appsettings). If it is changed to a different Shoptet state ID, the error message will still say "Balí se", silently giving the user the wrong context. This is an inconsistency with the adjacent `NonPrintableStates` pattern, which does maintain the ID-to-name mapping correctly.

## Suggested fix
Add the desired state name to `PrintPickingListOptions` as a companion property (e.g. `DesiredStateName`), or include the desired state's ID and name in `NonPrintableStates` when the options are resolved. The smallest change is adding `public string DesiredStateName { get; set; } = "Balí se";` to `PrintPickingListOptions` and referencing `_options.Value.DesiredStateName` in the handler response.

---
_Filed by daily arch-review routine on 2026-08-29._
