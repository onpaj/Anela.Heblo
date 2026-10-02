---
process: module-expedition-list-archive
kind: module
module: expedition-list-archive
summary: Archive of every printed picking list by day, with download and one-click reprint to the warehouse printer.
owns: []
verified_at: "5e993f9e2"
related:
  - flow-picking-list
  - flow-expedition-list-reprint
---

# Expedition list archive (Archiv expedice)

## Purpose
Keeps a copy of every picking list (expediční list) that was printed, organised by day, so the
warehouse can look back at what was printed, download a list, or print it again when a paper
copy is lost. It is read-only towards Shoptet: reprinting never changes orders.

## Users & screens
- Warehouse staff, page **Tisk expedice** `/logistics/expedition-archive` (feature
  `Warehouse_Expedition`): a paged list of days (20 per page, newest first); selecting a day shows
  its PDFs (list ID, file name, created time, size) with **open** (downloads the PDF and opens it
  in a new browser tab) and **Přetisk** (reprint, confirm dialog; Write access). The top bar of the same page belongs to the expedition-list module.
- No MCP tools.

## Processes
- `flow-expedition-list-reprint` — on demand: browse days, list PDFs, download, reprint a stored PDF to the CUPS printer.

Browsing and downloading are read-only and covered in the same doc. No scheduled jobs.

## Data owned
None. The blob container (`expedition-lists`, `expedition-lists-stg` outside production) is
written by the expedition-list module's print sink; this module only reads it.

## External systems
- **Azure Blob Storage**, read: list virtual directories, list blobs by prefix, download
  (through FileStorage's `IBlobStorageService`, connection `FileStorage:BlobConnectionString`).
- **CUPS** (vmHebloInfra), write: reprint via the keyed `"cups"` print sink.

## Dependencies
- Expedition list — produces the archived PDFs (`flow-picking-list`, `Combined`/`AzureBlob` sink)
  and owns the print-sink registration (`ExpeditionList:PrintSink`).
- FileStorage — `ExpeditionListArchiveBlobStoreAdapter` implements this module's
  `IExpeditionListArchiveBlobStore`.
- Nothing reads from this module.

## Known quirks
- Where no CUPS sink is registered (`AzureBlob` sink: staging, development), reprint falls back to the blob sink and uploads a `{guid}.pdf` copy into today's folder instead of printing, while reporting success.
- Container name (`ExpeditionListArchive:BlobContainerName`) and connection string (`FileStorage:BlobConnectionString`) are configured separately from the uploader's `ExpeditionList:*` keys; a mismatch shows an empty archive.
- No retention or cleanup; the archive grows forever.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/ExpeditionListArchiveModule.cs` — DI, reprint sink selection
- `backend/src/Anela.Heblo.API/Controllers/ExpeditionListArchiveController.cs` — endpoints
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/BlobPathValidator.cs` — allowed blob paths
- `frontend/src/pages/ExpeditionListArchivePage.tsx` — page
