---
process: flow-expedition-list-reprint
kind: workflow
module: expedition-list-archive
summary: Lets warehouse staff browse archived picking-list PDFs by day in Azure Blob storage, download one, or send it to the warehouse printer again via CUPS.
owns:
  - backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/**
  - backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/ExpeditionListArchiveBlobStoreAdapter.cs
  - backend/src/Anela.Heblo.API/Controllers/ExpeditionListArchiveController.cs
verified_at: "5e993f9e2"
related:
  - flow-picking-list
---

# Picking list archive & reprint (Přetisk expedice)

## Purpose
When a printed picking list (expediční list) is lost, jammed in the printer or needs a second
copy, staff open **Tisk expedice** (`/logistics/expedition-archive`), pick the day, and either
download the PDF or click reprint — the exact same PDF goes to the warehouse printer again.
Nothing in Shoptet changes: the orders are not re-read and their state is not touched. The
archive itself is filled by `flow-picking-list` (production `Combined` print sink uploads every
batch PDF before printing it).

## Trigger
On demand from the page (all endpoints under `[FeatureAuthorize(Feature.Warehouse_Expedition)]`):

| Action | Endpoint | Access |
|---|---|---|
| List days (20 per page, newest first) | `GET /api/expedition-list-archive/dates?page=&pageSize=` | Read |
| List PDFs of a day | `GET /api/expedition-list-archive/{yyyy-MM-dd}` | Read |
| Download a PDF | `GET /api/expedition-list-archive/download/{yyyy-MM-dd}/{file}.pdf` | Read |
| Reprint (confirm dialog "Odeslat … znovu na tiskárnu?") | `POST /api/expedition-list-archive/reprint` `{blobPath}` | **Write** |

No scheduled job.

## Data flow
1. **Days**: `IBlobStorageService.ListVirtualDirectoriesAsync(container)` → keep names that parse as
   `yyyy-MM-dd` → sort descending → page in memory.
2. **PDFs of a day**: `ListBlobsAsync(container, prefix = date)` → keep `*.pdf` → DTO with
   `BlobPath` (`{date}/{file}`), `FileName`, `ListId` (file name without `.pdf`, the "ID:" printed in
   the PDF header), `CreatedOn`, `ContentLength`.
3. **Download**: validate path → `DownloadAsync(container, blobPath)` → streamed as `application/pdf`.
4. **Reprint**: validate path → download blob → write to a temp file `{guid}.pdf`
   (`FileSystemTemporaryFileAccessor`) → send to the keyed `"cups"` `IPrintQueueSink`
   (`CupsPrintQueueSink` → IPP Print-Job to `{Cups:ServerUrl}/printers/{Cups:PrinterName}`) →
   delete the temp file.

Container: `ExpeditionListArchive:BlobContainerName`. Storage account: the shared FileStorage
`BlobServiceClient` (`FileStorage:BlobConnectionString`, KV `FileStorage--BlobConnectionString`) —
not `ExpeditionList:BlobConnectionString`, which the uploader uses.

## Logic & formulas
- **Path validation** (`BlobPathValidator`): must match `^\d{4}-\d{2}-\d{2}/[^/]+\.pdf$`
  (case-insensitive), contain no `..`, and the date must be a real date. Invalid → download returns
  400, reprint returns a failed response; nothing is read.
- Day list date filter is `yyyy-MM-dd` only; other folders in the container are ignored.
- Invalid `{date}` on the day listing → `InvalidFormat` error (`Field=Date`, `ExpectedFormat=yyyy-MM-dd`).
- Folder date and file name both use Prague local time of the run (`TZ=Europe/Prague` in the
  container), so a run at 03:00 lands in that day's folder.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `ExpeditionListArchive:BlobContainerName` | `expedition-lists`; Staging/Dev/Test `expedition-lists-stg` | Container the archive reads |
| `FileStorage:BlobConnectionString` | empty (KV `FileStorage--BlobConnectionString`) | Storage account read by the archive |
| `ExpeditionList:PrintSink` | `AzureBlob`; Production `Combined` | Decides whether a `"cups"` sink exists for reprint |
| `Cups:ServerUrl` / `PrinterName` | Tailscale URL / `Brother-HL-L2442DW` | Printer used by reprint |

## Runtime facts
None

## Known quirks
- **Reprint without CUPS re-uploads instead of printing.** The handler takes the keyed `"cups"`
  sink and falls back to the default sink when there is none. With `PrintSink = AzureBlob`
  (staging, development — the repo default) the fallback is `AzureBlobPrintQueueSink`, so
  "reprint" uploads the temp file as `{today}/{guid}.pdf` into the archive and prints nothing,
  while the UI reports success. With `FileSystem` it copies to `PrintQueueFolder`. Only `Cups`
  and `Combined` (production) really print.
- The archive and the uploader read their container name and connection string from two
  different config sections; they must point at the same account/container or the page shows
  nothing.
- No retention: blobs are never deleted by Heblo; the day list loads every folder name and pages in memory.
- Reprint prints the PDF as it was rendered — stock, prices and badges are those of the original run.

## Code entry points
- `backend/src/Anela.Heblo.API/Controllers/ExpeditionListArchiveController.cs` — endpoints and access levels
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListHandler.cs` — reprint
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/ExpeditionListArchiveModule.cs` — keyed `"cups"` sink with fallback
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/BlobPathValidator.cs` — path rules
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionDates/GetExpeditionDatesHandler.cs` — day list
- `backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/ExpeditionListArchiveBlobStoreAdapter.cs` — blob access
- `frontend/src/pages/ExpeditionListArchivePage.tsx` — page
