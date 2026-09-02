# Specification: Fix stale "ProductExportDownload" log label in DownloadFromUrlHandler

## Summary
`DownloadFromUrlHandler` (module-level FileStorage infrastructure) hardcodes the stale label `"ProductExportDownload"` in three log statements, a name carried over from an earlier, narrower use case. This spec covers replacing those three strings with the generic, accurate label `"DownloadFromUrl"` so log-based search and alerting correctly match all callers of this handler. Logging text only — no behavior, signature, or return-value change.

## Background
`DownloadFromUrlHandler` originated as a product-export-specific downloader and was later generalized into shared FileStorage infrastructure used by any caller that needs to fetch a URL into blob storage. Three log statements were never updated when the handler was generalized and still say `"ProductExportDownload"`, which is now a misleading, domain-specific label on generic infrastructure code. Operators searching logs by the old label will miss failures from other callers; operators searching by the correct operation name will miss these entries. This was filed by the automated `arch-review` routine (issue #4023).

## Functional Requirements

### FR-1: Replace hardcoded "ProductExportDownload" log label with "DownloadFromUrl"
In `backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs`, update the three log statements that currently reference `"ProductExportDownload"`:
- Line 118 (`Handle`, generic-exception catch block): `_logger.LogError(ex, "Unexpected failure during ProductExportDownload for URL: {RedactedUrl}", redactedUrl);` → `_logger.LogError(ex, "Unexpected failure during DownloadFromUrl for URL: {RedactedUrl}", redactedUrl);`
- Line 141 (`ProbeContentLengthAsync`, timeout catch block): `_logger.LogDebug("HEAD probe timed out for ProductExportDownload");` → `_logger.LogDebug("HEAD probe timed out for DownloadFromUrl");`
- Line 143 (`ProbeContentLengthAsync`, generic-exception catch block): `_logger.LogDebug(ex, "HEAD probe failed for ProductExportDownload");` → `_logger.LogDebug(ex, "HEAD probe failed for DownloadFromUrl");`

**Acceptance criteria:**
- All three log statements read `"...DownloadFromUrl..."` instead of `"...ProductExportDownload..."`.
- No other text, structured-logging placeholders (`{RedactedUrl}`), log levels, exception arguments, or surrounding logic in the file are changed.
- No occurrence of the literal string `"ProductExportDownload"` remains anywhere in `DownloadFromUrlHandler.cs`.
- `dotnet build` succeeds with no new warnings/errors.
- Existing unit tests for `DownloadFromUrlHandler` (if any assert on log message content) still pass; if a test asserts the old string literal, it is updated to assert the new one.

## Non-Functional Requirements

### NFR-1: Performance
None — no code path, allocation, or control flow changes; only string literal content changes.

### NFR-2: Security
None — no change to redaction behavior (`RedactUrl` is untouched); the fix does not add, remove, or alter what data is logged, only the fixed-text operation label.

## Data Model
Not applicable — no persisted or transmitted data structures are affected.

## API / Interface Design
Not applicable — no public contract, request/response DTO, or endpoint is affected. This is an internal log-message text change inside a MediatR request handler.

## Dependencies
None beyond the existing file itself. No other file in the codebase is known to reference the string `"ProductExportDownload"` in connection with this handler (to be confirmed by a repo-wide search during architecture review, per the issue's "no other callers rely on this string" assumption).

## Out of Scope
- Extracting the operation name into a shared constant or deriving it from the request (the issue's "suggested fix" mentions this as an alternative but selects the simpler direct-literal-replacement approach).
- Any change to log levels, log destinations, structured-logging schema, or alerting/dashboard configuration that may currently key off the string `"ProductExportDownload"` (out of scope for this codebase-only fix; if such external log-based alerts exist, they are a follow-up for the alert owner, not this change).
- Any behavior change to `DownloadFromUrlHandler`, `DownloadFromUrlRequest`, `DownloadFromUrlResponse`, or any other FileStorage component.

## Open Questions
None.

## Status: COMPLETE
