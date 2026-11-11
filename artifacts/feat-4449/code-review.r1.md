## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/MockBlobStorageService.cs` — `MockBlobInfo.SourceUrl` is no longer populated now that the mock's `DownloadFromUrlAsync` is gone; it can be dropped in a follow-up.

Reviewed the full diff against spec.r1.md: `DownloadAndStoreAsync` preserves the previous GET/upload semantics (named client, ResponseHeadersRead, EnsureSuccessStatusCode, URL redaction in logs, rethrow so the resilience pipeline and Handle's failure mapping are unchanged); the interface, adapter and constants removals leave no dangling references; build, format and FileStorage tests are green.
