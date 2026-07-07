# Implementation: relocate-document-extractors

## What was implemented
Moved `IDocumentTextExtractor` and its three implementations (`PdfTextExtractor`, `WordDocumentExtractor`, `PlainTextExtractor`) from `Anela.Heblo.Application.Features.KnowledgeBase.Services(.DocumentExtractors)` to `Anela.Heblo.Application.Shared.Rag(.DocumentExtractors)`, and updated every consumer's `using` statements accordingly. DI registration in `KnowledgeBaseModule.cs` was left in place (imports only) — moving registration ownership is a later task.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Shared/Rag/IDocumentTextExtractor.cs` — moved from KnowledgeBase.Services, namespace updated
- `backend/src/Anela.Heblo.Application/Shared/Rag/DocumentExtractors/{PdfTextExtractor,WordDocumentExtractor,PlainTextExtractor}.cs` — moved, namespace updated, logic untouched
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs` — `using` swapped to `Shared.Rag`/`Shared.Rag.DocumentExtractors`
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/{UploadLeaflet/UploadLeafletHandler,IndexLeaflet/IndexLeafletHandler}.cs` — `using` swapped to `Shared.Rag`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentHandler.cs` — `using` swapped to `Shared.Rag`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/DocumentIndexingService.cs` — added explicit `using Anela.Heblo.Application.Shared.Rag;`
- `backend/test/Anela.Heblo.Tests/Shared/Rag/DocumentExtractors/{PdfTextExtractorTests,WordDocumentExtractorTests,PlainTextExtractorTests}.cs` — moved from `KnowledgeBase/Services`, namespace + usings updated
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/{IndexLeafletHandlerTests,IndexLeafletStatusTransitionTests,UploadLeafletHandlerTests}.cs` — `using` swapped
- `backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/UploadDocumentHandlerTests.cs` — `using` swapped
- `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/DocumentIndexingServiceTests.cs` — added `using Anela.Heblo.Application.Shared.Rag;`, kept `KnowledgeBase.Services` using
- `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs` — **drive-by fix**: line 95 referenced `ConfigurationConstants.APP_VERSION`, a member that does not exist (`ConfigurationConstants` only has `DEFAULT_VERSION`/`DEFAULT_ENVIRONMENT`). This was a pre-existing compile error on `main` unrelated to this task, blocking `dotnet build` entirely. Fixed to `InfrastructureConfigurationKeys.APP_VERSION`, matching every other usage in the same file.

## Tests
Moved test files retain their original coverage for the three extractors. Ran the full set of tests touching these types:
```
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Shared.Rag.DocumentExtractors|FullyQualifiedName~DocumentIndexingServiceTests|FullyQualifiedName~UploadDocumentHandlerTests|FullyQualifiedName~UploadLeafletHandlerTests|FullyQualifiedName~IndexLeafletHandlerTests|FullyQualifiedName~IndexLeafletStatusTransitionTests"
```
Result: **Passed! Failed: 0, Passed: 55, Skipped: 0, Total: 55**

## How to verify
1. `dotnet build Anela.Heblo.sln` from the repo root — builds with 0 errors.
2. Run the test filter above — 55/55 pass.

## Notes
The plan's build/test commands assumed `cd backend && dotnet build Anela.Heblo.sln`, but the solution file lives at the worktree root, not under `backend/`; commands were run as `dotnet build Anela.Heblo.sln` from the root instead. No functional deviation from the plan otherwise — every step was followed as specified.

## Status
DONE
