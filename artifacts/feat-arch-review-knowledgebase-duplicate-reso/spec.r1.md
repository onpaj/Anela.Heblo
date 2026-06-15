# Specification: Consolidate Duplicate `ResolveContentType` Logic in KnowledgeBase Module

## Summary
Eliminate duplicate `ResolveContentType` implementations in `UploadDocumentHandler` and `IndexDocumentHandler` by extracting the MIME-type resolution logic into a single shared static helper. This removes a silent-divergence risk when new file extensions are added and clarifies intent by ensuring resolution happens exactly once per ingestion flow.

## Background
The KnowledgeBase module supports document ingestion through two entry points:

1. **User upload flow** — `UploadDocumentHandler` receives a file, resolves its MIME type, then dispatches an `IndexDocumentRequest` to `IndexDocumentHandler`.
2. **Background ingestion job** — calls `IndexDocumentHandler` directly (no prior upload step).

Both handlers currently contain identical private static `ResolveContentType` methods that map `application/octet-stream` to a specific MIME type based on file extension (`.pdf`, `.docx`, `.doc`, `.txt`, `.md`). The duplication creates two concrete risks:

- **Silent divergence:** Adding a new extension (e.g. `.xlsx`) requires editing both files. Forgetting one means the upload flow and the ingestion job return different MIME types for the same file.
- **Obscured intent:** In the upload flow, resolution effectively runs twice — once in `UploadDocumentHandler` before dispatching, once in `IndexDocumentHandler` on the already-resolved value (a no-op). Readers must verify the second call is harmless.

This is a low-risk, high-clarity refactor with no behavioral change for currently supported extensions.

## Functional Requirements

### FR-1: Single Source of Truth for Content-Type Resolution
A new internal static helper class `ContentTypeResolver` must exist at `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/ContentTypeResolver.cs`, exposing a single public method `Resolve(string contentType, string filename)`.

The method must preserve the existing behavior exactly:
- If `contentType` is non-empty and not `application/octet-stream` (case-insensitive), return `contentType` unchanged.
- Otherwise, map the lower-invariant file extension to the corresponding MIME type:
  - `.pdf`  → `application/pdf`
  - `.docx` → `application/vnd.openxmlformats-officedocument.wordprocessingml.document`
  - `.doc`  → `application/msword`
  - `.txt`  → `text/plain`
  - `.md`   → `text/markdown`
  - any other extension → the original `contentType` (unchanged fallback)

**Acceptance criteria:**
- `ContentTypeResolver.Resolve` exists at the specified path and is `internal static`.
- Unit tests cover each supported extension, the `application/octet-stream` trigger, the empty/null `contentType` trigger, the unsupported-extension fallback, the non-octet-stream pass-through, and case-insensitive comparison of `contentType` and extension.
- No other `ResolveContentType` method remains in the KnowledgeBase module.

### FR-2: `UploadDocumentHandler` Uses Shared Resolver
`UploadDocumentHandler` must delegate to `ContentTypeResolver.Resolve` and remove its private `ResolveContentType` method.

**Acceptance criteria:**
- The private `ResolveContentType` method at lines 73–84 of `UploadDocumentHandler.cs` is deleted.
- The call site that previously invoked the local method now calls `ContentTypeResolver.Resolve(...)` with identical arguments.
- The upload flow's externally observable behavior — the `ContentType` stored on the indexed document and returned in any response — is unchanged for all currently supported extensions and for unsupported extensions.

### FR-3: `IndexDocumentHandler` Trusts Pre-Resolved Input from Upload Flow
Because `UploadDocumentHandler` resolves the content type before dispatching `IndexDocumentRequest`, the resolution inside `IndexDocumentHandler` is a no-op in the upload flow. `IndexDocumentHandler` must also call `ContentTypeResolver.Resolve` so that the **direct-invocation path** (background ingestion job) remains correct.

**Acceptance criteria:**
- The private `ResolveContentType` method at lines 141–152 of `IndexDocumentHandler.cs` is deleted.
- `IndexDocumentHandler` calls `ContentTypeResolver.Resolve(request.ContentType, request.FileName)` (or equivalent) at the same point in the flow where resolution previously occurred.
- Both entry points — user upload via `UploadDocumentHandler` and direct dispatch to `IndexDocumentHandler` (e.g. from the ingestion job) — produce identical resolved MIME types for identical inputs.
- Calling `Resolve` twice on the same input is idempotent (verified by unit test).

### FR-4: Backward Compatibility for Unsupported Extensions
For file extensions not in the mapping table, the resolver must return the original `contentType` argument unchanged — even when that argument is `application/octet-stream`. This preserves the current behavior where unknown binary files retain their generic MIME type.

**Acceptance criteria:**
- A unit test verifies that `Resolve("application/octet-stream", "file.xyz")` returns `"application/octet-stream"`.
- A unit test verifies that `Resolve("image/png", "file.png")` returns `"image/png"` (non-octet-stream pass-through, regardless of extension).

## Non-Functional Requirements

### NFR-1: Performance
No measurable performance regression. The resolver is a synchronous pure function with O(1) dictionary-equivalent dispatch; call frequency is bounded by document ingestion rate (low). No allocations beyond the existing `Path.GetExtension` and `ToLowerInvariant` calls.

### NFR-2: Security
No change to security posture. Content-type resolution does not cross a trust boundary, does not influence authorization, and the mapping table is hard-coded (no user-controlled input affects the mapping).

### NFR-3: Maintainability
Adding a new supported extension must require editing exactly one file (`ContentTypeResolver.cs`) and adding one unit test case. No handler-level changes should be needed for future extension additions.

### NFR-4: Test Coverage
The new `ContentTypeResolver` class must reach the project-wide minimum of 80% line and branch coverage. Given the helper's small surface area, full coverage is expected and required.

## Data Model
No data model changes. The `ContentType` field on the existing document/index entities continues to store a MIME-type string with identical semantics.

## API / Interface Design

### New internal API
```csharp
namespace Anela.Heblo.Application.Features.KnowledgeBase;

internal static class ContentTypeResolver
{
    public static string Resolve(string contentType, string filename);
}
```

- **Visibility:** `internal` — used only within the `Anela.Heblo.Application` assembly.
- **Location:** `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/ContentTypeResolver.cs`.
- **No DI registration** — static helper, called directly.

### Modified handlers
- `UploadDocumentHandler.cs`: removes private `ResolveContentType`; call site updated to `ContentTypeResolver.Resolve(...)`.
- `IndexDocumentHandler.cs`: removes private `ResolveContentType`; call site updated to `ContentTypeResolver.Resolve(...)`.

### Public API
No public API changes. No request/response DTOs change. No OpenAPI client regeneration is needed.

## Dependencies
- **Runtime:** `System.IO.Path` (already referenced) for `Path.GetExtension`.
- **Test framework:** xUnit (project convention) for the new unit-test class.
- **No new NuGet packages.**
- **No external services** affected.
- **No coordination with frontend** — purely backend internal refactor.

## Out of Scope
- Adding support for new file extensions (e.g. `.xlsx`, `.pptx`, `.csv`). This refactor preserves the existing mapping verbatim; extending the mapping is a separate change.
- Making the mapping configurable (JSON config, database, feature flag). The current hard-coded table is sufficient.
- Replacing the custom resolver with a third-party MIME library (e.g. `MimeTypes`, `HeyRed.Mime`). Out of scope; would expand the change surface beyond the duplication fix.
- Refactoring other duplicated logic in the KnowledgeBase module not called out in the brief.
- Changes to the `IndexDocumentRequest` DTO or `IndexDocumentHandler` flow beyond the call-site swap.
- Renaming the existing method, parameters, or related variables in the handlers beyond what is needed to invoke the new helper.

## Open Questions
None.

## Status: COMPLETE