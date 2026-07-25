# Design: Move `CombinedPrintQueueSink` into the Azure Adapter Project

## Component Design
No new components. `CombinedPrintQueueSink` relocates from `Anela.Heblo.API/Features/ExpeditionList/` to `Anela.Heblo.Adapters.Azure/Features/ExpeditionList/`, alongside `AzureBlobPrintQueueSink`.

- **Namespace:** `Anela.Heblo.API.Features.ExpeditionList` → `Anela.Heblo.Adapters.Azure.Features.ExpeditionList`.
- **Visibility:** `internal sealed class` → `public` (sealed optional), required so `ServiceCollectionExtensions` in the API assembly can construct it directly.
- **Contract:** implements `IPrintQueueSink` (`Anela.Heblo.Application.Shared.Printing`), unchanged.
- **Constructor / behavior:** `(IPrintQueueSink azureSink, IPrintQueueSink cupsSink)` and `SendAsync` body are byte-for-byte unchanged.
- **Call site:** `ServiceCollectionExtensions.AddPrintQueueSink`, `"Combined"` case, updated to reference the type in its new namespace (via the existing `using Anela.Heblo.Adapters.Azure.Features.ExpeditionList;`); the now-unused `using Anela.Heblo.API.Features.ExpeditionList;` is removed.

## Data Schemas
N/A — no data model, API surface, or event payload is affected by this change.
