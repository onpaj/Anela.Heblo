Final review approved. All 4 tasks complete. Now I'll write the output summary artifact.

---

# Implementation: ContentTypeResolver Consolidation — KnowledgeBase Module

## What was implemented

Extracted the duplicate `ResolveContentType` private methods from `UploadDocumentHandler` and `IndexDocumentHandler` into a single shared `internal static ContentTypeResolver` helper at the KnowledgeBase module root. Both handlers now delegate to `ContentTypeResolver.Resolve(...)`. The resolution call is preserved in `IndexDocumentHandler` because the background ingestion job calls it directly without prior resolution (the double-call in the upload flow is idempotent and harmless).

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/ContentTypeResolver.cs` — new `internal static` helper, 17 lines; pure function mapping MIME types from file extensions
- `backend/test/Anela.Heblo.Tests/KnowledgeBase/ContentTypeResolverTests.cs` — new test class, 23 test cases covering all extensions, empty/null contentType, unknown extension fallback, non-octet-stream pass-through, case-insensitivity, and idempotency
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentHandler.cs` — call site updated to `ContentTypeResolver.Resolve(...)`, private method deleted, `using` added
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/IndexDocument/IndexDocumentHandler.cs` — call site updated to `ContentTypeResolver.Resolve(...)`, private method deleted, `using` added

## Tests

- `ContentTypeResolverTests.cs`: 23 tests — known extension mapping (5), empty contentType (2), null contentType (1), unknown extension fallback (1), non-octet pass-through (3), case-insensitivity (3), idempotency (8)
- Existing `UploadDocumentHandlerTests`: 5 tests — all passing, no changes
- Existing `IndexDocumentHandlerTests`: 15 tests — all passing, no changes
- Total KnowledgeBase test suite: 43 tests passing

## How to verify

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/.worktrees/feat-arch-review-knowledgebase-duplicate-reso
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~KnowledgeBase"
dotnet build backend/Anela.Heblo.sln
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```

## Notes

- No `ResolveContentType` remains in the KnowledgeBase module (verified by grep)
- `InternalsVisibleTo("Anela.Heblo.Tests")` was already present — no project file changes needed
- `UploadLeafletHandler` in the Leaflet module still has an identical copy of this logic — explicitly out of scope per the arch review, documented as a known follow-up
- Formatting clean: `dotnet format --verify-no-changes` exited 0

## PR Summary

Consolidated duplicate `ResolveContentType` logic in the KnowledgeBase module into a single `internal static ContentTypeResolver` helper, eliminating the silent-divergence risk when new file extensions are added. The mapping table now lives in exactly one place; adding a new extension requires editing one file and one test.

### Changes
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/ContentTypeResolver.cs` — new shared helper
- `backend/test/Anela.Heblo.Tests/KnowledgeBase/ContentTypeResolverTests.cs` — 23 tests covering all branches and idempotency
- `backend/src/...UseCases/UploadDocument/UploadDocumentHandler.cs` — swapped call site, deleted private method
- `backend/src/...UseCases/IndexDocument/IndexDocumentHandler.cs` — swapped call site, deleted private method

## Status

DONE