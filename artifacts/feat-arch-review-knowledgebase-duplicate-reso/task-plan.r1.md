Plan saved to `docs/superpowers/plans/2026-06-13-kb-content-type-resolver-consolidation.md`.

**Summary:** 4 tasks, TDD-ordered:
1. Create `ContentTypeResolver` + full test matrix (mapping table, empty/null contentType, unsupported-extension fallback, non-octet pass-through, case-insensitivity, idempotency).
2. Switch `UploadDocumentHandler` to the shared resolver and delete its private method.
3. Switch `IndexDocumentHandler` to the shared resolver and delete its private method (kept in place because `KnowledgeBaseIngestionJob` calls it directly).
4. Validation gate: grep for surviving `ResolveContentType`, run all KB tests, `dotnet build`, `dotnet format`.

`InternalsVisibleTo("Anela.Heblo.Tests")` is already present in `Anela.Heblo.Application.csproj`, so no project-file change is needed. The Leaflet copy of `ResolveContentType` is explicitly flagged as out-of-scope per the arch review.