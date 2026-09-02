# Architecture Review: Fix stale "ProductExportDownload" log label in DownloadFromUrlHandler

## Skip Design: true

## Architectural Fit Assessment
This is a pure text correction inside a single leaf-level MediatR request handler (`DownloadFromUrlHandler`), part of the module-level `FileStorage` infrastructure. It touches no public contract, no interface, no DI registration, and no cross-module boundary. It aligns trivially with existing patterns — no architectural risk, no design work, no new components.

Verified against the codebase:
- `grep -rn "ProductExportDownload" backend/` shows exactly two independent usages of the string:
  1. `Anela.Heblo.Application/Features/Catalog/Infrastructure/Jobs/ProductExportDownloadJob.cs` — a genuinely product-export-specific Hangfire recurring job (class name, telemetry event name `"ProductExportDownload"`, and a failure log message). This is a *correct*, domain-accurate use of the label for its own bounded context and is **not** part of this change.
  2. `Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs` (lines 118, 141, 145 as currently numbered in the working tree — the issue's line numbers 118/141/143 refer to the same three statements, off by the blank/comment lines) — the stale, generalized-away label targeted by this fix.
  3. `AzureBlobStorageServiceTests.cs` has a test named `DownloadFromUrlAsync_ResolvesNamedClient_ProductExportDownload` — this is a **test method name**, not a log assertion, and does not reference the handler's log text; it is unrelated and out of scope.
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/DownloadFromUrlHandlerTests.cs` contains no assertions on log message text (confirmed via search for `LogError`/`LogDebug`/`ProductExportDownload` — no matches), so no test changes are required as part of this fix.
- No config, alert rule, or dashboard definition in this repository references the string `"ProductExportDownload"` in connection with `DownloadFromUrlHandler` (none found under `backend/`; alerting infrastructure, if any, lives outside this repo and is out of scope per the spec).

## Proposed Architecture

### Component Overview
No component, interface, or dependency changes. Single file touched:

```
DownloadFromUrlHandler.cs
├── Handle(...)                     — catch(Exception) block: fix log line (was line 118)
└── ProbeContentLengthAsync(...)
    ├── catch(OperationCanceledException) — fix log line (was line 141)
    └── catch(Exception)                  — fix log line (was line 143/145)
```

### Key Design Decisions

#### Decision 1: Direct literal replacement vs. extracting a shared constant
**Options considered:**
- (a) Replace the three literal strings in place with `"DownloadFromUrl"`, matching the issue's suggested fix.
- (b) Introduce a `const string OperationName = "DownloadFromUrl";` field and reference it in all three call sites.
- (c) Derive the label dynamically from `nameof(DownloadFromUrlHandler)` or from `request` state.

**Chosen approach:** (a) — direct literal replacement, exactly as specified in the spec and the issue's suggested fix.

**Rationale:** The three log statements are not duplicated elsewhere and are unlikely to drift again independently (this class was already generalized once; a `const` doesn't prevent a future contributor from writing a *new* ad hoc literal in a different handler). Introducing a constant or `nameof` indirection is a larger touch than the bug warrants — three literal strings in one file, changed once — and the issue explicitly frames this as a logging-only, no-behavior-change fix. Keep the diff minimal and match the spec.

## Implementation Guidance

### Directory / Module Structure
No new files, no new directories. Single existing file modified:
`backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs`

### Interfaces and Contracts
None affected. `IRequestHandler<DownloadFromUrlRequest, DownloadFromUrlResponse>` signature, `DownloadFromUrlResponse` shape, and all public/internal contracts are unchanged.

### Data Flow
Unchanged. This fix does not alter control flow, branching, retry behavior, or return values — only the literal text passed as the log message template in three `ILogger` calls.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| A downstream log-based alert or dashboard (outside this repo) keys on the literal string `"ProductExportDownload"` appearing in these three log lines and silently stops firing after the rename | Low | Out of scope for this repo-only fix per the spec; flag to the operator/alert owner if such an external rule is known to exist. No such reference exists in this repository. |
| Typo introduced while editing (e.g. `"DownloadFromURL"` casing mismatch) | Low | Use exact casing `"DownloadFromUrl"` (matches the class/namespace `DownloadFromUrl` casing used throughout the file, e.g. `DownloadFromUrlHandler`, `DownloadFromUrlRequest`). Verify post-edit with `grep -n "ProductExportDownload" DownloadFromUrlHandler.cs` returning no results and `grep -n "DownloadFromUrl\"" DownloadFromUrlHandler.cs` returning the three updated lines. |

## Specification Amendments
None. The spec (`spec.r1.md`) is accurate and sufficient as written; this review confirms its assumptions (no other callers/tests reference the old string in this file) against the actual codebase.

## Prerequisites
None. No migrations, config, or infrastructure changes are needed before implementation can start.
