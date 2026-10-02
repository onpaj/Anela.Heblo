---
process: module-file-storage
kind: module
module: file-storage
summary: Heblo's connection to Azure Blob Storage — downloads a file from a URL into a blob container with retries (used for the nightly Shoptet product export) and gives the expedition-list archive read access to the stored packing-list PDFs.
owns: []
verified_at: "f68c439ec"
related: []
---

# File storage (Azure Blob Storage)

## Purpose
Heblo keeps some files outside its database, in Azure Blob Storage. This module is the shared
plumbing for that; it has no business screen of its own. It does two things for other modules:

1. **Download a file from a web address and store it as a blob** — with timeouts and retries.
   The only scheduled user is the Catalog job `product-export-download` (daily 02:00), which
   saves the Shoptet product export CSV as `products_yy_MM_dd_HH_mm.csv` into the container
   named by `ProductExport:ContainerName`. That job is documented by the Catalog module.
2. **Read access to stored files for the expedition-list archive** (Archiv expedičních listů):
   list the date folders, list the PDFs of one day, download a PDF for viewing or reprinting.
   The PDFs themselves are written by the expedition-list print queue (see below), not by this
   module.

## Users & screens
No page. One admin endpoint for manual use:
- `POST /api/FileStorage/download` (`FileStorageController`, requires `Admin_Administration`
  Read) body `{ fileUrl, containerName, blobName? }` → `{ blobUrl, blobName, containerName,
  fileSizeBytes }`. Not called by the frontend.

No MCP tool, no dashboard tile.

## Processes
No process doc of its own — the module has no recurring job. Its download use case runs inside
the Catalog job `product-export-download` (`ProductExportDownloadJob`), owned and documented by
the Catalog module.

How the download works (`DownloadFromUrlHandler` → `DownloadResilienceService` →
`AzureBlobStorageService.DownloadFromUrlAsync`):
1. Validation (`DownloadFromUrlRequestValidator`): `fileUrl` must be an absolute http/https URL
   (else error `InvalidUrlFormat` 1801); `containerName` must be a valid Azure container name —
   3–63 chars, lowercase letters/digits/hyphens, starts and ends with a letter or digit, no `--`
   (else `InvalidContainerName` 1802).
2. Best-effort `HEAD` request (timeout `HeadTimeout`, 10 s) to learn the file size; failures are
   ignored and the size is reported as 0.
3. `GET` the URL with the named HttpClient `FileDownload` (gzip/brotli decompression, sockets
   recycled every 5 min, no HttpClient-level timeout) and stream the body straight into the blob.
   The container is created if missing (private access). The blob is **overwritten** if it exists.
   Blob name = `blobName`, else the file name from the URL path, else
   `downloaded-file-<guid><ext>` (extension from the content type). Content type = response
   header, else guessed from the extension.
4. Each attempt has a timeout of `DownloadTimeout` (120 s). Retries: up to `MaxRetryAttempts`
   (3) more attempts, exponential backoff from `RetryBaseDelay` (2 s) with jitter, on
   `HttpRequestException`, timeout, or an internal cancellation. Each retry is logged and sent to
   Application Insights as an exception with `Job = FileDownload`, `AttemptNumber`,
   `IsTerminal = false`. Start-up fails if `DownloadTimeout × (MaxRetryAttempts + 1)` ≥ 20 min.
5. Failure returns `FileDownloadFailed` (1803) with params `fileUrl` (query string removed, so
   tokens in the URL are not logged), `cause` (`timeout` / `http-status` / `retry-exhausted`),
   `attemptCount`, `elapsedMs`, `error`.

## Data owned
Blob containers in the storage account given by `FileStorage:BlobConnectionString`:
- Product export container (`ProductExport:ContainerName`, not set in repo appsettings) — daily
  CSV snapshots written by the download above. Nothing deletes old snapshots.
- Expedition-list containers (`ExpeditionListArchive:BlobContainerName`, repo default
  `expedition-lists`, `expedition-lists-stg` on Staging/Development/Test) — this module only
  **reads** them for the archive; folders are dates `yyyy-MM-dd/`, files are the printed PDFs.

No database tables.

## External systems
| System | Direction | What |
|---|---|---|
| Azure Blob Storage (`FileStorage:BlobConnectionString`) | write + read | Upload (unconditional overwrite), list, list top-level "folders", download stream, exists, delete, blob URL |
| Any HTTP(S) URL given to the download (in practice the Shoptet product export URL, `ProductExport:Url`) | read | `HEAD` + `GET` |

Configuration (repo defaults):

| Key | Default | Note |
|---|---|---|
| `FileStorage:BlobConnectionString` | placeholder in `appsettings.json`, `""` in Production/Staging json (real value from Key Vault), `UseDevelopmentStorage=true` in Development/Test | Outside Development, an empty value stops the app at start-up (`ValidateOnStart`) |
| `FileStorage:Download:HeadTimeout` | 00:00:10 | not in appsettings; code default |
| `FileStorage:Download:DownloadTimeout` | 00:02:00 | code default |
| `FileStorage:Download:MaxRetryAttempts` | 3 | code default |
| `FileStorage:Download:RetryBaseDelay` | 00:00:02 | code default |

## Dependencies
- Used by **Catalog** (`ProductExportDownloadJob` sends `DownloadFromUrlRequest` through MediatR)
  and **ExpeditionListArchive** (through its own contract `IExpeditionListArchiveBlobStore`,
  implemented here by `ExpeditionListArchiveBlobStoreAdapter`).
- The Azure adapter project (`Anela.Heblo.Adapters.Azure`) also contains the **expedition-list
  print queue sink** (`AzureBlobPrintQueueSink`, `CombinedPrintQueueSink`): when
  `ExpeditionList:PrintSink` is `AzureBlob` or `Combined`, every printed packing list PDF is
  uploaded to `ExpeditionList:BlobContainerName` under `yyyy-MM-dd/<file>` (local date,
  overwrite). That is the source of the archive. It uses its **own** connection string
  `ExpeditionList:BlobConnectionString`, not `FileStorage:BlobConnectionString`, and belongs to
  the ExpeditionList module's printing process.

## Known quirks
- **4xx responses are retried.** The retry predicate handles every `HttpRequestException`, and
  `EnsureSuccessStatusCode` raises it for 404/403 too, so a wrong or expired export URL costs
  4 attempts with backoff before failing, and the failure is reported with
  `cause = retry-exhausted` rather than `http-status` (see agent memory
  `gotcha_retrying_httprequestexception_retries_4xx`, 2026-09-22).
- Blob-storage errors (`RequestFailedException`, e.g. bad credentials or a full account) are not
  retried and surface as `cause = retry-exhausted` with `attemptCount = 1`.
- Two connection strings point at blob storage (`FileStorage:…` and `ExpeditionList:…`). The
  archive **reads** with the first and the print queue **writes** with the second; if they ever
  point at different accounts the archive is silently empty.
- The archive and product-export containers have no retention; blobs accumulate forever.
- `fileSizeBytes` comes only from the HEAD probe; servers that don't answer HEAD (or omit
  `Content-Length`) report 0 even when the download succeeded.
- Containers created by the upload are cached per process as "exists"; if one is deleted while
  the app runs, uploads to it fail until restart.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs` — download use case, failure params
- `backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/DownloadResilienceService.cs` — retry/timeout policy
- `backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs` — options, HttpClient, start-up validation
- `backend/src/Anela.Heblo.Domain/Features/FileStorage/IBlobStorageService.cs` — storage contract
- `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/FileStorage/AzureBlobStorageService.cs` — Azure implementation
- `backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs` — client registration, print-queue sink registration
- `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/AzureBlobPrintQueueSink.cs` — PDF upload for the archive (ExpeditionList-owned)
- `backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/ExpeditionListArchiveBlobStoreAdapter.cs` — archive read contract
