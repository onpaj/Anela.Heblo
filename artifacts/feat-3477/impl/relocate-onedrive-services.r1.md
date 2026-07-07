# Implementation: relocate-onedrive-services

## What was implemented
Moved `IOneDriveService`/`OneDriveFile`, `GraphOneDriveService`, `MockOneDriveService`, `GraphFolderResolver`, and the mis-filed `GraphApiHelpers.cs` (renamed `GraphDriveModels.cs`) from `Anela.Heblo.Application.Features.KnowledgeBase.Services` to `Anela.Heblo.Application.Shared.Rag(.OneDrive)`, and updated every consumer's `using` statements. DI registration in `KnowledgeBaseModule.cs` remains in place for now (imports only) — moving DI ownership is task 3.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Shared/Rag/IOneDriveService.cs` — moved, namespace updated, `OneDriveFile` kept as a `record` (internal service type, not an API DTO)
- `backend/src/Anela.Heblo.Application/Shared/Rag/OneDrive/{GraphOneDriveService,GraphFolderResolver,MockOneDriveService}.cs` — moved, namespace updated, `Common.Graph` using preserved unchanged
- `backend/src/Anela.Heblo.Application/Shared/Rag/OneDrive/GraphDriveModels.cs` — moved and renamed from `GraphApiHelpers.cs`, namespace updated, contents (`GraphDriveItem`/`GraphFileFacet`/`GraphDriveItemCollection`) unchanged
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs` — added `using Anela.Heblo.Application.Shared.Rag.OneDrive;`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/Jobs/KnowledgeBaseIngestionJob.cs` — using swapped to `Shared.Rag`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/KnowledgeBaseArticleStyleGuideSource.cs` — using swapped to `Shared.Rag`
- `backend/src/Anela.Heblo.Application/Features/Leaflet/Infrastructure/Jobs/LeafletIngestionJob.cs` — using swapped to `Shared.Rag`
- `backend/test/Anela.Heblo.Tests/Common/HebloWebApplicationFactory.cs` — added `Shared.Rag`/`Shared.Rag.OneDrive` usings
- `backend/test/Anela.Heblo.Tests/Shared/Rag/OneDrive/GraphOneDriveServiceTests.cs` — moved from `KnowledgeBase/Services`, namespace + using updated
- `backend/test/Anela.Heblo.Tests/KnowledgeBase/Infrastructure/KnowledgeBaseIngestionJobTests.cs` — using swapped
- `backend/test/Anela.Heblo.Tests/Features/KnowledgeBase/Infrastructure/KnowledgeBaseArticleStyleGuideSourceTests.cs` — using swapped
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/Infrastructure/LeafletIngestionJobTests.cs` — using swapped

## Tests
```
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Shared.Rag.OneDrive|FullyQualifiedName~KnowledgeBaseIngestionJobTests|FullyQualifiedName~KnowledgeBaseArticleStyleGuideSourceTests|FullyQualifiedName~LeafletIngestionJobTests"
```
Result: **Passed! Failed: 0, Passed: 23, Skipped: 0, Total: 23**

## How to verify
1. `dotnet build Anela.Heblo.sln` from the repo root — builds with 0 errors.
2. Run the test filter above — 23/23 pass.

## Notes
Build/test commands run from the worktree root (`dotnet build Anela.Heblo.sln`, not `cd backend && ...` as the plan text literally says) since the `.sln` lives at the worktree root, matching the same correction applied in task 1.

## Status
DONE
