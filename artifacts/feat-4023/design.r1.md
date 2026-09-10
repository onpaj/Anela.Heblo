# Design: Fix stale "ProductExportDownload" log label in DownloadFromUrlHandler

## Component Design
No new or restructured components. The only component involved is the existing `DownloadFromUrlHandler` (`Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl`), a MediatR `IRequestHandler<DownloadFromUrlRequest, DownloadFromUrlResponse>`. Its responsibility (download a file from a URL and upload it to blob storage) is unchanged. The change is confined to the text of three `ILogger` calls inside two existing methods:

- `Handle(...)` — `catch (Exception ex)` block: operation-name text in the `LogError` message template.
- `ProbeContentLengthAsync(...)` — `catch (OperationCanceledException)` and `catch (Exception ex)` blocks: operation-name text in the two `LogDebug` message templates.

All three currently read `"...ProductExportDownload..."` and become `"...DownloadFromUrl..."`. No new dependencies, parameters, or structured-logging properties are introduced; the existing `{RedactedUrl}` placeholder and its argument are untouched.

## Data Schemas
Not applicable — no database schema, API request/response shape, or event payload is affected. The log message template is free-text (not a structured field consumed by any DTO or contract), so no schema definition changes.
