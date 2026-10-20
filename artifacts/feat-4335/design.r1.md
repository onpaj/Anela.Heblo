# Design: Move Azure Blob Print-Sink Config Out of PrintPickingListOptions

## Component Design

### `PrintPickingListOptions` (modified) — `Anela.Heblo.Application.Features.ExpeditionList`
Responsibility: hold the Application-layer, business-semantic configuration for expedition list printing/state-transition behavior. After this change it declares only the properties its actual consumers read:

| Property | Consumed by |
|---|---|
| `EmailSender` | `ExpeditionListService` |
| `PrintQueueFolder` | `FileSystemPrintQueueSink` (via `PrintPickingListOptions`, unchanged — out of scope) |
| `DefaultEmailRecipients` | `ExpeditionListService` |
| `SourceStateId` | `ExpeditionListService` |
| `FixSourceStateId` | `RunExpeditionListPrintFixHandler` |
| `DesiredStateId` | `ExpeditionListService`, `PrintExpeditionOrderHandler` |
| `DesiredStateName` | `PrintExpeditionOrderHandler` |
| `NoteStateId` | `ExpeditionListService` |
| `SendToPrinterByDefault` | `ExpeditionListService` |
| `ChangeOrderStateByDefault` | `ExpeditionListService` |

It no longer declares `PrintSink`, `BlobConnectionString`, or `BlobContainerName`. `ConfigurationKey = "ExpeditionList"` is unchanged — both this class and the new `AzureBlobPrintSinkOptions` below bind from that same section (see arch-review Decision 2).

### `AzureBlobPrintSinkOptions` (new) — `Anela.Heblo.Adapters.Azure.Features.ExpeditionList`
Responsibility: hold the Azure Blob Storage-specific configuration needed to construct the `BlobContainerClient` used by `AzureBlobPrintQueueSink`. Owned entirely by the Adapters.Azure layer; no Application-layer code references it.

| Property | Default | Consumed by |
|---|---|---|
| `BlobConnectionString` | `string.Empty` | `AzureAdapterModule.AddAzurePrintQueueSinkInfrastructure` (constructs `BlobContainerClient`) |
| `BlobContainerName` | `"expedition-lists"` | `AzureAdapterModule.AddAzurePrintQueueSinkInfrastructure` (constructs `BlobContainerClient`) |

Bound via `services.Configure<AzureBlobPrintSinkOptions>(configuration.GetSection(PrintPickingListOptions.ConfigurationKey))` inside `AddAzurePrintQueueSinkInfrastructure` itself — the only method that needs it, and the only place it is registered (see arch-review Decision 3). Not consumed anywhere else; `AzureBlobPrintQueueSink` itself still takes a plain `BlobContainerClient` in its constructor and is unaware of either options class.

### `AzureAdapterModule.AddAzurePrintQueueSinkInfrastructure` (modified) — `Anela.Heblo.Adapters.Azure`
Responsibility unchanged: register the `BlobContainerClient` singleton and `AzureBlobPrintQueueSink` singleton needed by both the `"AzureBlob"` and `"Combined"` print-sink modes. Only its internal options dependency changes, from `IOptions<PrintPickingListOptions>` to `IOptions<AzureBlobPrintSinkOptions>`. Its public signature (`(IServiceCollection, IConfiguration) -> IServiceCollection`) and both call sites in `ServiceCollectionExtensions.AddPrintQueueSink` are unchanged.

### Unaffected components (explicitly not touched)
`ServiceCollectionExtensions.AddPrintQueueSink`, `ExpeditionListModule`, `ExpeditionListService`, `PrintExpeditionOrderHandler`, `RunExpeditionListPrintFixHandler`, `AzureBlobPrintQueueSink`, `CombinedPrintQueueSink`, `FileSystemPrintQueueSink`, `FileSystemAdapterServiceCollectionExtensions`, `ExpeditionListArchiveOptions` and its four consuming handlers, `CupsPrintQueueSink`. See arch-review "Implementation Guidance → Directory / Module Structure" for the full unaffected list.

## Data Schemas

No database schema, API contract, or event payload changes. The only "schema" affected is the shape of two in-memory C# configuration POCOs bound from the existing `appsettings*.json` `"ExpeditionList"` JSON object — the JSON object's shape on disk (and in Key Vault) is byte-for-byte unchanged:

```json
// appsettings.json — "ExpeditionList" section — UNCHANGED by this design
{
  "ExpeditionList": {
    "EmailSender": "heblo@anela.cz",
    "PrintQueueFolder": "PDFPrints",
    "SourceStateId": -2,
    "FixSourceStateId": 73,
    "DesiredStateId": 26,
    "DesiredStateName": "Balí se",
    "NoteStateId": 35,
    "SendToPrinterByDefault": true,
    "ChangeOrderStateByDefault": true,
    "PrintSink": "AzureBlob",
    "BlobConnectionString": "...",
    "BlobContainerName": "expedition-lists"
  }
}
```

```csharp
// Two C# projections of the same JSON object, after this design:

// Anela.Heblo.Application.Features.ExpeditionList.PrintPickingListOptions
// picks up: EmailSender, PrintQueueFolder, SourceStateId, FixSourceStateId,
//           DesiredStateId, DesiredStateName, NoteStateId,
//           SendToPrinterByDefault, ChangeOrderStateByDefault
// ignores:  PrintSink, BlobConnectionString, BlobContainerName (no longer declared)

// Anela.Heblo.Adapters.Azure.Features.ExpeditionList.AzureBlobPrintSinkOptions
// picks up: BlobConnectionString, BlobContainerName
// ignores:  everything else in the section (not declared)
```

`PrintSink` is picked up by neither options class — it continues to be read as a raw string via `configuration["ExpeditionList:PrintSink"]` in `ServiceCollectionExtensions.AddPrintQueueSink`, unchanged.
