# Implementation: define-smartsupp-knowledge-source-contract-and-adapter

## What was implemented
Added the Smartsupp-owned `ISmartsuppKnowledgeSource` contract and the KnowledgeBase-owned `KnowledgeBaseSmartsuppKnowledgeSource` adapter that implements it by delegating to the existing `SearchDocumentsRequest`/`SearchDocumentsHandler` MediatR flow, mirroring the existing `IArticleKnowledgeSource` / `KnowledgeBaseArticleKnowledgeSource` pattern. Registered the DI binding in `KnowledgeBaseModule`.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/Contracts/ISmartsuppKnowledgeSource.cs` — new Smartsupp-owned interface + `SmartsuppKnowledgeChunk` DTO
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/KnowledgeBaseSmartsuppKnowledgeSource.cs` — new internal adapter implementing the interface via `IMediator`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs` — added `using` for Smartsupp contracts and `services.AddScoped<ISmartsuppKnowledgeSource, KnowledgeBaseSmartsuppKnowledgeSource>()`
- `backend/test/Anela.Heblo.Tests/KnowledgeBase/Infrastructure/KnowledgeBaseSmartsuppKnowledgeSourceTests.cs` — new adapter unit tests

## Tests
`dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~KnowledgeBaseSmartsuppKnowledgeSourceTests"` → Passed! - Failed: 0, Passed: 4, Skipped: 0, Total: 4.

## How to verify
Run the filtered test command above, then `dotnet build Anela.Heblo.sln` (build succeeded, no new compile errors).

## Notes
Full solution build showed the pre-existing `AccessMatrixGen` MSBuild `Exec` step throwing (unrelated, `ContinueOnError="true"`, argument-order bug already present in `Anela.Heblo.API.csproj` before this change) — build still succeeds overall.

## Status
DONE
