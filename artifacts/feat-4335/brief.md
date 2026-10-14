## Module
ExpeditionList

## Finding
`PrintPickingListOptions` lives in the Application layer (`backend/src/Anela.Heblo.Application/Features/ExpeditionList/PrintPickingListOptions.cs`) but contains three fields that are purely infrastructure / adapter concerns:

| Property | Line | Actual consumer |
|---|---|---|
| `PrintSink` (line 17) | dead — no code reads `options.Value.PrintSink`; `ServiceCollectionExtensions.AddPrintQueueSink` reads `configuration["ExpeditionList:PrintSink"]` directly (line 437) | none |
| `BlobConnectionString` (line 18) | `AzureAdapterModule.AddAzurePrintQueueSinkInfrastructure` — outer Adapters layer | Azure adapter |
| `BlobContainerName` (line 19) | same | Azure adapter |

The Application-layer handlers (`PrintExpeditionOrderHandler`, `RunExpeditionListPrintFixHandler`) and the service (`ExpeditionListService`) inject `IOptions<PrintPickingListOptions>` and consume only the application-semantic fields: `DesiredStateId`, `DesiredStateName`, `FixSourceStateId`, `SourceStateId`, `NoteStateId`, `EmailSender`, `DefaultEmailRecipients`, `SendToPrinterByDefault`, `ChangeOrderStateByDefault`. None of them ever touch `BlobConnectionString`, `BlobContainerName`, or `PrintSink`.

## Why it matters
1. **Clean Architecture layering**: an Application-layer class should not hold configuration properties that exist solely for an Infrastructure/Adapter adapter. Adding a new print adapter (e.g., a network printer) currently requires editing an Application-layer file, not an adapter file.
2. **Dead property**: `PrintSink` is never read via the `IOptions` pipeline. A developer looking at `PrintPickingListOptions` can reasonably believe it controls adapter selection at runtime — it does not. The actual switch-dispatch reads `configuration["ExpeditionList:PrintSink"]` raw.
3. **Secret surface area**: `BlobConnectionString` sits in the Application layer's config section. If a future developer adds options validation (`ValidateDataAnnotations`/`ValidateOnStart`) to `PrintPickingListOptions`, the Azure connection string must be present even in environments with no blob storage.

## Suggested fix
1. Remove `PrintSink`, `BlobConnectionString`, and `BlobContainerName` from `PrintPickingListOptions`.
2. Add a new `AzureBlobPrintSinkOptions` class in `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/` bound to the same config section (or a sub-section like `ExpeditionList:AzureBlob`). Read `BlobConnectionString` and `BlobContainerName` from it inside `AzureAdapterModule`.
3. `PrintSink` needs no replacement — it is already read as a raw config string in `ServiceCollectionExtensions.AddPrintQueueSink`.

This is a small structural change; no business logic moves.

---
_Filed by daily arch-review routine on 2026-09-27._
