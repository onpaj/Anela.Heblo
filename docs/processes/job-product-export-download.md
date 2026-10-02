---
process: job-product-export-download
kind: job
module: catalog
summary: Nightly copies a product export file from a configured URL into Azure Blob Storage as a timestamped CSV snapshot; nothing in Heblo reads the copies.
owns:
  - backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/Jobs/ProductExportDownloadJob.cs
  - backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/ProductExportOptions.cs
verified_at: "5e993f9e2"
related: []
---

# Product export snapshot (nightly)

## Purpose
Keeps a dated archive of the product export (presumably the Shoptet product CSV — the URL is
configuration only, so the exact source cannot be determined from the repo) so the state of the
catalogue on a given night can be looked up later. No Heblo page, report or MCP tool reads the
archived files; they are for manual inspection in the storage account.

## Trigger
Hangfire recurring job `product-export-download`, cron `0 2 * * *` (02:00 Europe/Prague),
enabled by default, category Catalog. Can be run by hand from Recurring Jobs. Skipped (telemetry
`Status=Skipped`) when disabled there. `[AutomaticRetry(Attempts = 0)]`: Hangfire never retries.

## Data flow
1. Read `ProductExportOptions:Url`; empty → the job throws "Product export URL is not
   configured".
2. Send `DownloadFromUrlRequest` (FileStorage module) with `FileUrl` = that URL,
   `ContainerName` = `ProductExportOptions:ContainerName`, `BlobName` =
   `products_{yy_MM_dd_HH_mm}.csv` (UTC).
3. The FileStorage handler probes the size with HEAD, then streams the file into the Azure Blob
   container through its own retry policy (`IDownloadResilienceService`).
4. Telemetry event `ProductExportDownload` with `Status` (Success / Failed / Cancelled /
   Skipped), attempt count, elapsed ms, file name, blob URL and size; on failure also
   `ErrorCode` and `Cause` (`timeout`, `http-status`, `retry-exhausted`). A failure is re-thrown
   so Hangfire shows the run as Failed.

## Logic & formulas
No transformation — a byte-for-byte copy. One new blob per run; old blobs are never deleted by
Heblo.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `ProductExportOptions:Url` | "" (empty in `appsettings.json`) | Export URL; must come from Key Vault / environment |
| `ProductExportOptions:ContainerName` | not set in any appsettings | Target blob container |
| Hangfire job `product-export-download` | `0 2 * * *`, enabled | Schedule |

## Runtime facts
None.

## Known quirks
- **Nothing consumes the archive.** The job only produces blobs; no code reads `products_*.csv`.
- **Both settings are empty in the repo.** Without `Url` the job fails every night; without
  `ContainerName` the blob upload gets a null container. The FileStorage validator for container
  names is registered but no `ValidationBehavior` runs it for this request.
- Runs at the same minute as `product-weight-recalculation` (`feed-product-weight`).
- `ProductExportOptions` is deliberately bound in `CatalogModule` (ADR in
  `memory/decisions/product-export-options-ownership.md`); do not move it to FileStorage.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/Jobs/ProductExportDownloadJob.cs` — job, telemetry, failure handling
- `backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs` — download + upload
- `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/FileStorage/AzureBlobStorageService.cs` — blob write
