### task: use-redactor-in-handler-and-adapter
Depends on add-url-redactor.
1. `DownloadFromUrlHandler.cs`: replace `RedactUrl(request.FileUrl)` with `UrlRedactor.Redact(request.FileUrl)` and delete the private `RedactUrl` method (lines ~155-166). Check for other `RedactUrl` callers in the file.
2. `AzureBlobStorageService.cs`: before `try` in `DownloadFromUrlAsync` add `var redactedUrl = UrlRedactor.Redact(fileUrl);`; use it in the "Starting download from URL" LogInformation and in the LogError. Do not change the URL passed to HttpClient or `new Uri(fileUrl)`.
3. Add test(s) in `AzureBlobStorageServiceTests.cs` using the existing logger-mock pattern: with a URL containing `?token=secret`, assert no log entry message/state contains `secret` on both success path and failure path (HttpClient returns error).
Verify: `dotnet build backend`, `dotnet format backend --verify-no-changes`, `dotnet test backend/test/Anela.Heblo.Tests --filter "FileStorage"` all pass.
