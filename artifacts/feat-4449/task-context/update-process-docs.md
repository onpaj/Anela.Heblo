### task: update-process-docs
- `docs/processes/module-file-storage.md`: rewrite step 3 of "How the download works" and the header chain to `DownloadFromUrlHandler` (GET + upload inside the retry delegate) -> `IBlobStorageService.UploadAsync`; remove references to `AzureBlobStorageService.DownloadFromUrlAsync`; adjust the "Code entry points" description for `IBlobStorageService.cs` (pure blob contract) and `AzureBlobStorageService.cs` (blob operations only, no HTTP); set `verified_at` to the current HEAD short SHA.
- `docs/processes/job-product-export-download.md`: change the `AzureBlobStorageService.cs` entry to "blob write (upload)" and the step 3 wording so the handler does HEAD, GET and upload; bump `verified_at`.
- Run `python3 scripts/process-docs/check.py index` (and `check.py` default check if available) and fix any complaints.
Verify: the process-docs check exits 0.

