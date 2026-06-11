## Module
ExpeditionList

## Finding
`CombinedPrintQueueSink` lives in `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/CombinedPrintQueueSink.cs` (lines 11–13) and resolves its two sinks via keyed-service attributes:

```csharp
[FromKeyedServices("azure")] IPrintQueueSink azureSink,
[FromKeyedServices("cups")] IPrintQueueSink cupsSink
```

The string keys `"azure"` and `"cups"` are composition-root decisions defined in `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` (lines 411–419). Baking them into an Application-layer class means the Application layer is coupled to infrastructure keying conventions — if either key is renamed in the composition root, the class silently breaks at runtime with no compile-time warning.

## Why it matters
Clean Architecture requires the Application layer to depend on abstractions only; knowing *how* adapters are keyed in the DI container is a composition-root (infrastructure) concern, not an application concern. This violates the inward dependency rule: Application → Domain only; the composition root owns adapter wiring.

## Suggested fix
Move `CombinedPrintQueueSink` to the composition root as an inline factory, or to the API layer, and strip the `[FromKeyedServices]` attributes from its constructor:

```csharp
// ServiceCollectionExtensions.AddPrintQueueSink, "Combined" case:
case "Combined":
    services.AddAzurePrintQueueSink(configuration);
    services.AddKeyedScoped<IPrintQueueSink, AzureBlobPrintQueueSink>("azure");
    services.AddKeyedScoped<IPrintQueueSink, CupsPrintQueueSink>("cups");
    services.AddScoped<IPrintQueueSink>(provider =>
    {
        var azure = provider.GetRequiredKeyedService<IPrintQueueSink>("azure");
        var cups  = provider.GetRequiredKeyedService<IPrintQueueSink>("cups");
        return new CombinedPrintQueueSink(azure, cups);
    });
    break;
```

`CombinedPrintQueueSink` then takes two plain `IPrintQueueSink` constructor parameters and can live in the API layer (or be inlined entirely).

---
_Filed by daily arch-review routine on 2026-06-06._