# Architecture Review: Redact URLs in AzureBlobStorageService logs

## Skip Design: true
Backend-only logging fix, no UI.

## Architectural Fit Assessment
Fits Clean Architecture: Adapters.Azure and Application both depend on Domain, so a shared static helper in Domain avoids duplication without a new dependency edge. Verified: `FileStorageConstants` lives in `backend/src/Anela.Heblo.Domain/Features/FileStorage/` and is already used by the adapter; handler's `RedactUrl` is private static at DownloadFromUrlHandler.cs:155.

## Proposed Architecture

### Component Overview
Domain: `UrlRedactor.Redact(string?)` <- used by `DownloadFromUrlHandler` and `AzureBlobStorageService`.

### Key Design Decisions
#### Decision 1: Where the helper lives
**Options considered:** (a) copy private helper into adapter; (b) constant class `FileStorageConstants`; (c) new static class in Domain FileStorage.
**Chosen approach:** (c).
**Rationale:** constants class is the wrong home for behaviour; copy duplicates security logic.

#### Decision 2: Redaction scope
Strip query, fragment and user-info; fall back to `[redacted]` on any parse failure. Superset of current handler behaviour, so existing handler tests stay valid (path and host unchanged).

## Implementation Guidance
### Directory / Module Structure
- add `backend/src/Anela.Heblo.Domain/Features/FileStorage/UrlRedactor.cs`
- edit `DownloadFromUrlHandler.cs` (remove private RedactUrl, call helper)
- edit `AzureBlobStorageService.cs` (two log calls; compute redacted once before try so catch can use it)
- tests in `backend/test/Anela.Heblo.Tests/Features/FileStorage/`

### Interfaces and Contracts
`public static string Redact(string? url)` never throws, returns `[redacted]` for null/empty/invalid.

### Data Flow
Raw URL still goes to HttpClient; only log arguments use redacted value.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Handler tests assert exact redacted string format | Low | Keep `UriBuilder(...).Uri.ToString()` output for query-only URLs; run existing handler tests |
| HttpClient exception message contains full URL and is logged via LogError(ex) | Medium | Out of scope; mention in PR description as follow-up |

## Specification Amendments
None.

## Prerequisites
None.
