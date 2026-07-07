# Code Review: relocate-document-extractors

## Summary
The implementation moves `IDocumentTextExtractor` and its three implementations from `Features.KnowledgeBase.Services(.DocumentExtractors)` to `Shared.Rag(.DocumentExtractors)` exactly as specified, with every consumer's `using` statements updated correctly (including the one file, `DocumentIndexingService.cs`, that needed a new explicit `using` since it previously relied on being declared in the same namespace). I independently verified every diff against the step-by-step spec, confirmed `dotnet build` succeeds with 0 errors, and re-ran the exact targeted test filter, which passed (55/55).

## Review Result: PASS

### task: relocate-document-extractors
**Status:** PASS

## Overall Notes
- Verified via `git diff-tree` that all 4 files (`IDocumentTextExtractor.cs`, `PdfTextExtractor.cs`, `WordDocumentExtractor.cs`, `PlainTextExtractor.cs`) were moved with `git mv` semantics (tracked as D+A, content identical apart from the namespace line) into `backend/src/Anela.Heblo.Application/Shared/Rag(.DocumentExtractors)`, and the old `Features/KnowledgeBase/Services/DocumentExtractors` directory no longer exists.
- Verified `KnowledgeBaseModule.cs` swaps the `using` exactly as specified and leaves the `services.AddScoped<IDocumentTextExtractor, ...>()` registration lines untouched (correctly deferred to a later task).
- Verified the two Leaflet handlers, `UploadDocumentHandler.cs`, and `DocumentIndexingService.cs` all have the `using` changes matching the spec's exact before/after snippets.
- Verified the 3 extractor test files were moved to `backend/test/Anela.Heblo.Tests/Shared/Rag/DocumentExtractors/` with corrected namespace/usings, and the 4 remaining consumer test files (`IndexLeafletHandlerTests`, `IndexLeafletStatusTransitionTests`, `UploadLeafletHandlerTests`, `UploadDocumentHandlerTests`) had their using swapped.
- Verified `DocumentIndexingServiceTests.cs` kept the old `KnowledgeBase.Services` using (still needed for `IIndexingStrategy`/`DocumentIndexingService`) and added the new `Shared.Rag` using.
- Ran `dotnet build Anela.Heblo.sln` myself: succeeded, 0 errors (254 pre-existing unrelated nullable-reference warnings only).
- Ran the exact targeted test filter from the spec myself: `Passed! - Failed: 0, Passed: 55, Skipped: 0, Total: 55` — matches the impl summary's claim.
- The reported "drive-by fix" in `GetConfigurationHandlerTests.cs` (swapping `ConfigurationConstants.APP_VERSION` → `InfrastructureConfigurationKeys.APP_VERSION`) was confirmed to be a genuine pre-existing compile error on the parent commit (unrelated to this task, blocking the whole solution from building), and the fix correctly matches the pattern used elsewhere in the same file. This is a reasonable, narrowly-scoped fix to unblock the build.
- Note (not blocking): the API project's post-build `AccessMatrixGen` step exits with code 134 (a JSON deserialization crash) during `dotnet build`, but this is logged only as an MSB3073 warning and does not fail the build or affect this task; it is pre-existing and unrelated to the extractor relocation.
