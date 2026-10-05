## Goal
Fix the code review findings below

## Blocking findings
- `backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs:42` — The handler's entry log still passes raw `request.FileUrl` (query string, user-info, fragment) to `LogInformation`, so secret-bearing URLs still reach logs on the same download path; this violates NFR-2 ("No secret-bearing URL components in logs from this path"). Compute `redactedUrl` before this log and log `redactedUrl` instead.
- `backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs:83` — The cancellation log (`"File download operation was cancelled for URL: {FileUrl}"`) also logs raw `request.FileUrl`. Log `redactedUrl` instead.
