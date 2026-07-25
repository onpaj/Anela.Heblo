## Module
ExpeditionList

## Finding
`CombinedPrintQueueSink` is placed in `backend/src/Anela.Heblo.API/Features/ExpeditionList/CombinedPrintQueueSink.cs` — the API host/composition layer.

`filesystem.md` is explicit:

> Concrete `IPrintQueueSink` implementations and any I/O-bound service live in adapter projects under `backend/src/Adapters/`, not in `Features/{Feature}/Services/`.

Every other `IPrintQueueSink` implementation respects this rule:

| Implementation | Location |
|---|---|
| `FileSystemPrintQueueSink` | `backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/Features/ExpeditionList/` |
| `AzureBlobPrintQueueSink` | `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/` |
| `CupsPrintQueueSink` | `backend/src/Adapters/Anela.Heblo.Adapters.Cups/Features/ExpeditionList/` |
| **`CombinedPrintQueueSink`** | **`backend/src/Anela.Heblo.API/Features/ExpeditionList/`** ← wrong layer |

The wiring in `ServiceCollectionExtensions.AddPrintQueueSink` (line 439) references it by its API-layer namespace:
```csharp
return new Anela.Heblo.API.Features.ExpeditionList.CombinedPrintQueueSink(azure, cups);
```

## Why it matters
Placing I/O adapters in the API host project breaks the Adapters pattern the codebase has otherwise consistently applied. It couples the API host to composition details that should live independently, makes the `CombinedPrintQueueSink` harder to test in isolation (it requires the API project), and will confuse anyone following `filesystem.md` to find all `IPrintQueueSink` implementations.

## Suggested fix
Move `CombinedPrintQueueSink` to one of the existing adapter projects — `Anela.Heblo.Adapters.Azure` is the natural home since it already carries `AzureBlobPrintQueueSink` (the primary sink it combines):

1. Add the class to `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/CombinedPrintQueueSink.cs` (update namespace accordingly).
2. Update the `using` and the factory lambda in `ServiceCollectionExtensions.AddPrintQueueSink` to reference the new namespace.
3. Delete the file from `API/Features/ExpeditionList/`.

No behaviour changes — just a file move and namespace update.

---
_Filed by daily arch-review routine on 2026-07-19._
