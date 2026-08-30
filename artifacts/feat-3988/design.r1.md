# Design: Derive `currentStatusName` for the desired-state case from configuration

## Component Design

### `PrintPickingListOptions` (backend/src/Anela.Heblo.Application/Features/ExpeditionList/PrintPickingListOptions.cs)
Existing options POCO bound from configuration section `"ExpeditionList"` via `IOptions<PrintPickingListOptions>`. Gains one new property:

```csharp
public string DesiredStateName { get; set; } = "Balí se";
```

Placed as a sibling immediately after `DesiredStateId` to keep the ID/name pairing visually and semantically adjacent, matching how `NonPrintableStates` pairs IDs with names elsewhere in the module. No behavioral change to any other property; no constructor or validation logic exists on this class today and none is added.

### `PrintExpeditionOrderHandler` (backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/PrintExpeditionOrder/PrintExpeditionOrderHandler.cs)
Single-line change in the desired-state branch of `Handle()`:

```csharp
// Before
{ "currentStatusName", "Balí se" },

// After
{ "currentStatusName", _options.Value.DesiredStateName },
```

Responsibility of the handler is otherwise unchanged: resolve the current Shoptet order status, short-circuit with `ErrorCodes.ExpeditionOrderInvalidState` if the order is already in the desired state or one of the other non-printable states, otherwise proceed to print. No new dependencies are injected — `_options` (already `IOptions<PrintPickingListOptions>`) is the only source needed for the new value.

## Data Schemas

### Configuration schema (`appsettings.json`, section `"ExpeditionList"`)
Additive key alongside the existing one, for documentation/symmetry (the C# default already covers the case where this key is absent):

```jsonc
"ExpeditionList": {
  ...
  "DesiredStateId": 26,              // Bali se
  "DesiredStateName": "Balí se",     // NEW — paired display name for DesiredStateId
  ...
}
```

### `PrintExpeditionOrderResponse` shape (unchanged)
No change to the response contract. `Params: Dictionary<string, string>` still carries `"orderCode"` and `"currentStatusName"` for the `ExpeditionOrderInvalidState` error code; only the runtime *value* placed under `"currentStatusName"` changes for the desired-state branch (from a literal to `_options.Value.DesiredStateName`). Frontend consumption (`frontend/src/i18n.ts:226`) requires no changes.
