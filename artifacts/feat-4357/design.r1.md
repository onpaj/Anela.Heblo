# Design: Redact URLs in AzureBlobStorageService logs

## Component Design
`Anela.Heblo.Domain.Features.FileStorage.UrlRedactor` (public static):
- `Redact(string? url)`: if null/whitespace return `[redacted]`; try `new UriBuilder(url) { Query = string.Empty, Fragment = string.Empty, UserName = string.Empty, Password = string.Empty }.Uri.ToString()` (note: UriBuilder Query = null is the existing approach; empty values also acceptable); catch Exception -> `[redacted]`.

Consumers:
- `DownloadFromUrlHandler`: `var redactedUrl = UrlRedactor.Redact(request.FileUrl);`, delete private `RedactUrl`.
- `AzureBlobStorageService.DownloadFromUrlAsync`: `var redactedUrl = UrlRedactor.Redact(fileUrl);` as first statement (before `try`); use in the "Starting download" log and in the LogError.

## Data Schemas
None.
