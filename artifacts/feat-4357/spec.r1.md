# Specification: Redact query strings from URLs logged by AzureBlobStorageService

## Summary
`AzureBlobStorageService.DownloadFromUrlAsync` logs the full source URL (including query string) on start and on error. This leaks tokens (SAS, API keys) into Application Insights and contradicts the redaction done by `DownloadFromUrlHandler.RedactUrl`. Redact the URL in the adapter's logs and share one helper.

## Background
Handler redacts via private `RedactUrl` (UriBuilder with Query = null, fallback "[redacted]"). Adapter logs `fileUrl` raw at the "Starting download" LogInformation and the LogError in the catch block.

## Functional Requirements

### FR-1: Adapter logs redacted URL
Both log calls in `DownloadFromUrlAsync` that reference `fileUrl` must use a redacted form.
**Acceptance criteria:**
- Log output for a URL with `?token=abc` never contains the query string or `abc`.
- Invalid/unparseable URLs log `[redacted]` and logging never throws.
- Fragment is also not logged (UriBuilder Query=null keeps fragment; acceptable unless it is stripped too - see FR-2).

### FR-2: Single shared redaction helper
Handler and adapter use one helper in the Domain FileStorage namespace (next to `FileStorageConstants`), replacing the handler's private `RedactUrl`.
**Acceptance criteria:**
- Only one implementation exists; handler behaviour is unchanged.
- Helper also strips user-info (user:pass@) and fragment.

### FR-3: Behaviour unchanged
Download/upload behaviour, exceptions and the actual request URL (which keeps its query) are unchanged.

## Non-Functional Requirements
### NFR-1: Performance
Negligible; one Uri parse per log call.
### NFR-2: Security
No secret-bearing URL components in logs from this path. Exception messages from HttpClient may still include URLs; out of scope (note in architecture review).

## Data Model
None.

## API / Interface Design
New `public static class UrlRedactor { public static string Redact(string? url); }` in `Anela.Heblo.Domain.Features.FileStorage`. No API/contract change.

## Dependencies
Adapter and Application both already reference Domain.

## Out of Scope
Other log sites in other modules; redacting HttpClient exception messages.
