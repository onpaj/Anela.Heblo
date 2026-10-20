# Implementation: use-redactor-in-handler-and-adapter (r1)
- DownloadFromUrlHandler: now calls `UrlRedactor.Redact`; private `RedactUrl` removed (no other callers).
- AzureBlobStorageService.DownloadFromUrlAsync: `redactedUrl` computed before `try`, used in the start LogInformation and the LogError. HttpClient/`new Uri(fileUrl)` still use the raw URL.
- Tests: two added to AzureBlobStorageServiceTests (success + failure path) using a capturing ILogger; assert no entry (message or state values) contains the query secret.
- Verified: dotnet build (0 errors), `dotnet test --filter FileStorage` 134 passed, dotnet format --verify-no-changes clean on touched files.
