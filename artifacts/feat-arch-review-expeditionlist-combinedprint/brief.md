## Module
ExpeditionList

## Finding
`CombinedPrintQueueSink` — a class that implements `IPrintQueueSink` and contains composite sink logic — is placed in the **API project** under `backend/src/Anela.Heblo.API/Features/ExpeditionList/CombinedPrintQueueSink.cs` (25 lines).

The class has no HTTP/controller responsibility whatsoever: it wraps two `IPrintQueueSink` implementations and delegates `SendAsync` to both. It is registered in the DI container as the non-keyed `IPrintQueueSink` when `PrintSink = "Combined"` is configured.

```csharp
// API/Features/ExpeditionList/CombinedPrintQueueSink.cs
internal sealed class CombinedPrintQueueSink : IPrintQueueSink
{
    public CombinedPrintQueueSink(
        [FromKeyedServices("azure")] IPrintQueueSink azureSink,
        [FromKeyedServices("cups")] IPrintQueueSink cupsSink) { ... }

    public async Task SendAsync(IEnumerable<string> filePaths, ...) { ... }
}
```

Every other `IPrintQueueSink` implementation lives in the Application layer (`Services/FileSystemPrintQueueSink.cs`, adapters in `Persistence/` or adapter projects). Only controllers belong in the API project per `docs/architecture/development_guidelines.md`.

## Why it matters
The API layer is supposed to be a thin HTTP shell — controllers, middleware, auth filters. Placing business-logic adapters there:
- Breaks Clean Architecture's dependency rule: the API project should depend on Application, not contain Application logic.
- Makes the sink untestable without the full API assembly being loaded.
- Sets a precedent that makes the API project a dumping ground for anything that touches DI.

The parallel sink implementations (`FileSystemPrintQueueSink`, `AzureBlobPrintQueueSink`, `CupsPrintQueueSink`) all live in Application or adapter projects — this is the one outlier.

## Suggested fix
Move `CombinedPrintQueueSink` to `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/CombinedPrintQueueSink.cs` and update the `internal sealed` visibility to `internal` (same as today). The DI registration in the API project's `Program.cs` can still reference it via the Application assembly — or if registration logic needs to stay in the API layer, expose a factory method in `ExpeditionListModule`.

---
_Filed by daily arch-review routine on 2026-06-03._