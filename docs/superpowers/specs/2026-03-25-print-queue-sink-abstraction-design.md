# Print Queue Sink Abstraction Design

**Date:** 2026-03-25
**Feature:** ExpeditionList – PrintQueueSink abstraction

## Summary

Introduce an `IPrintQueueSink` abstraction for the current hardcoded filesystem-based `SendToPrinter` logic in `ExpeditionListService`. Provide two implementations: a filesystem sink (debug/local) and an Azure Blob Storage sink (production). Selection is config-driven via DI registration in the API layer.

---

## Interface

**Location:** `Application/Features/ExpeditionList/Services/IPrintQueueSink.cs`

```csharp
public interface IPrintQueueSink
{
    Task SendAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default);
}
```

- Accepts file paths (strings), not domain objects
- Caller is responsible for reading the files; sink is responsible for delivering them

---

## Implementation 1: FileSystemPrintQueueSink

**Location:** `Application/Features/ExpeditionList/Services/FileSystemPrintQueueSink.cs`

- Extracts current `SendToPrinter` logic from `ExpeditionListService` as-is
- Reads `PrintQueueFolder` from `PrintPickingListOptions`
- Copies files to the configured folder
- Used for local development and debugging

---

## Implementation 2: AzureBlobPrintQueueSink

**New project:** `backend/src/Adapters/Anela.Heblo.Adapters.Azure/`

```
Anela.Heblo.Adapters.Azure/
├── Anela.Heblo.Adapters.Azure.csproj
├── AzureAdapterModule.cs
└── Features/
    └── ExpeditionList/
        ├── AzureBlobPrintQueueSink.cs
        └── AzureBlobPrintQueueOptions.cs
```

**Dependencies:** `Azure.Storage.Blobs`, references `Anela.Heblo.Application`

**Options** (`AzureBlobPrintQueueOptions`):
- `ConnectionString` — Azure Storage connection string
- `ContainerName` — blob container name (e.g. `expedition-lists`)

**Config section:** `ExpeditionListBlobStorage`

```json
"ExpeditionListBlobStorage": {
  "ConnectionString": "...",
  "ContainerName": "expedition-lists"
}
```

**Behavior:**
- Reads each file from disk, uploads bytes to blob storage
- Blob name: `yyyy-MM-dd/{fileName}`

---

## ExpeditionListService Changes

`ExpeditionListService` receives `IPrintQueueSink` via constructor injection. The private `SendToPrinter` method is replaced with:

```csharp
await _printQueueSink.SendAsync(result.ExportedFiles, cancellationToken);
```

---

## DI Registration (API Layer)

Registration lives in the API layer (or `ApplicationModule`) to avoid pulling Azure adapter dependencies into the Application project.

```csharp
var sink = configuration["ExpeditionList:PrintSink"];
if (sink == "AzureBlob")
    services.AddScoped<IPrintQueueSink, AzureBlobPrintQueueSink>();
else
    services.AddScoped<IPrintQueueSink, FileSystemPrintQueueSink>();
```

**`PrintPickingListOptions`** gains:

```csharp
public string PrintSink { get; set; } = "FileSystem"; // "FileSystem" | "AzureBlob"
```

`AzureAdapterModule` handles its own options binding and is only wired up when `AzureBlob` is selected.

---

## Out of Scope

- Runtime sink switching without restart
- Composite sink (sending to multiple destinations simultaneously)
- Blob lifecycle management / cleanup policies
