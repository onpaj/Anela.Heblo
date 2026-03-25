# Combined Print Queue Sink Design

**Date:** 2026-03-25
**Feature:** `ExpeditionList:PrintSink = "Combined"`
**Status:** Approved

---

## Overview

Add a new `"Combined"` print sink that uploads expedition list PDFs to Azure Blob Storage **and** sends them to CUPS for physical printing — in sequence, with fail-fast semantics. Both operations are equally important; failure of either propagates as an exception.

---

## Architecture

The existing print sink system follows the Strategy pattern:

```
IPrintQueueSink
├── FileSystemPrintQueueSink   (Application layer)
├── AzureBlobPrintQueueSink    (Azure adapter)
└── CupsPrintQueueSink         (CUPS adapter)
```

Selected at startup via `ExpeditionList:PrintSink` config value in `Program.cs`.

The new `CombinedPrintQueueSink` is a **thin combinator** that delegates to both concrete sinks in sequence. It lives in the API project (composition root) because that is the only layer that already references both adapters and is the correct place for wiring concerns.

---

## Components

### `CombinedPrintQueueSink`

**Location:** `backend/src/Anela.Heblo.API/Features/ExpeditionList/CombinedPrintQueueSink.cs`

- Implements `IPrintQueueSink`
- Constructor takes `AzureBlobPrintQueueSink` and `CupsPrintQueueSink` as direct concrete dependencies (not `IPrintQueueSink`) to avoid ambiguity
- Calls Azure first, then CUPS
- No exception handling — both failures propagate to the caller
- `internal sealed` — not part of any public API
- **Materializes `filePaths` to a `List<string>` at the top of `SendAsync`** before passing to either sink, because `IEnumerable<string>` may be a single-pass sequence; without materialization, CUPS would receive an empty sequence after Azure exhausts it

```csharp
public async Task SendAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default)
{
    var paths = filePaths.ToList();
    await _azureSink.SendAsync(paths, cancellationToken);
    await _cupsSink.SendAsync(paths, cancellationToken);
}
```

### `Program.cs` — `"Combined"` switch case

Calls the existing extension methods to register all required infrastructure (Azure blob client, CUPS HTTP client, auth handler, printing service, etc.). Those methods also register their respective sinks as `IPrintQueueSink`, but that binding is irrelevant here — we subsequently add two **separate concrete-type registrations** (`AddScoped<AzureBlobPrintQueueSink>()`, `AddScoped<CupsPrintQueueSink>()`) that allow `CombinedPrintQueueSink` to receive each sink directly via constructor injection. These concrete-type registrations are independent DI descriptors (keyed by the concrete type, not the interface); they resolve correctly because all constructor dependencies (`BlobContainerClient`, `ICupsPrintingService`, etc.) are already registered by the preceding extension methods. Finally, `CombinedPrintQueueSink` is registered as `IPrintQueueSink` — ASP.NET Core resolves the **last** `IPrintQueueSink` registration when a single instance is requested, so this is the one `ExpeditionListService` receives.

```csharp
case "Combined":
    builder.Services.AddAzurePrintQueueSink(builder.Configuration);   // registers Azure infra
    builder.Services.AddCupsAdapter(builder.Configuration);            // registers CUPS infra
    builder.Services.AddScoped<AzureBlobPrintQueueSink>();             // concrete-type entry for DI into CombinedPrintQueueSink
    builder.Services.AddScoped<CupsPrintQueueSink>();                  // concrete-type entry for DI into CombinedPrintQueueSink
    builder.Services.AddScoped<IPrintQueueSink, CombinedPrintQueueSink>(); // last IPrintQueueSink registration — wins
    break;
```

---

## Data Flow

```
PrintPickingListJob
  → ExpeditionListService.PrintPickingListAsync()
    → IPickingListSource.CreatePickingList()   [generates PDFs to temp folder]
    → IPrintQueueSink.SendAsync(filePaths)     [resolved as CombinedPrintQueueSink]
      → AzureBlobPrintQueueSink.SendAsync()   [upload to blob: {date}/{filename}]
      → CupsPrintQueueSink.SendAsync()        [send to CUPS via IPP]
    → cleanup temp files
```

---

## Error Handling

| Scenario | Behaviour |
|----------|-----------|
| Azure upload fails | Exception propagates; CUPS is never called |
| Azure succeeds, CUPS fails | Exception propagates |
| Both succeed | Normal completion |

No partial-success scenarios. The job-level error handling in `PrintPickingListJob` logs and surfaces failures.

---

## Configuration

No new configuration keys. The combined sink reuses existing sections:

| Key | Used by |
|-----|---------|
| `ExpeditionList:PrintSink = "Combined"` | Activates this sink |
| `ExpeditionListBlobStorage:ConnectionString` | Azure Blob |
| `ExpeditionListBlobStorage:ContainerName` | Azure Blob (default: `expedition-lists`) |
| `Cups:ServerUrl` | CUPS |
| `Cups:PrinterName` | CUPS |
| `Cups:Username` | CUPS |
| `Cups:Password` | CUPS |

---

## Testing

**File:** `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/CombinedPrintQueueSinkTests.cs`

Four test cases:

1. **Both succeed** — both sinks called with the same file paths (verifies `filePaths` materialization: pass two paths, assert CUPS received both)
2. **Azure throws** — CUPS never called; exception propagates
3. **Azure succeeds, CUPS throws** — exception propagates
4. **`filePaths` is single-pass** — pass a `yield return` sequence; assert both sinks receive all paths (guards against the IEnumerable exhaustion bug)

Both dependencies mocked with **Moq** (matching existing test patterns in `CupsPrintQueueSinkTests` and `AzureBlobPrintQueueSinkTests`).

---

## Out of Scope

- No retry logic (handled at job level)
- No parallel execution (sequential is intentional — Azure first for archival, CUPS for printing)
- No new configuration options
- No changes to existing sink implementations
