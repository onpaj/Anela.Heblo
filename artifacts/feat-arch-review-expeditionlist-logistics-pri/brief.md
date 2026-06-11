## Module
ExpeditionList (found via Logistics integration chain)

## Finding
`PrintPickingListRequest.DefaultCarriers` in `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListRequest.cs` (line 16) is declared with a public setter:

```csharp
public static IList<Carriers> DefaultCarriers { get; set; } = new List<Carriers>()
{
    Carriers.Zasilkovna, Carriers.GLS, Carriers.PPL, Carriers.Osobak
};
```

Any code can silently replace the entire default-carrier list with `PrintPickingListRequest.DefaultCarriers = someOtherList`. The change persists for the entire process lifetime.

Compare with `ExpeditionPickingRequest.DefaultCarriers` (the consumer-facing equivalent in `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/ExpeditionPickingRequest.cs`, line 16), which is read-only:

```csharp
public static IList<Carriers> DefaultCarriers { get; } = new List<Carriers> { ... };
```

Additionally, `PrintPickingListRequest.DefaultCarriers` is referenced only in an integration test (`backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/PickingListIntegrationTests.cs`, line 88). All production code (`PrintPickingListJob`, `RunExpeditionListPrintFixHandler`) uses `ExpeditionPickingRequest.DefaultCarriers` instead, so this property is dead in the production path.

## Why it matters
A public setter on a static property that holds shared mutable state is a global-variable mutation hazard. If any caller (including a test that forgets to restore state) writes to it, subsequent expedition list runs silently use a different carrier set with no diagnostic signal.

## Suggested fix
Remove the setter — change to `{ get; }`. Since it is not used in production code, also verify whether the property should be removed entirely and the integration test updated to use `ExpeditionPickingRequest.DefaultCarriers` directly (which is what production code already uses).

---
_Filed by daily arch-review routine on 2026-06-07._