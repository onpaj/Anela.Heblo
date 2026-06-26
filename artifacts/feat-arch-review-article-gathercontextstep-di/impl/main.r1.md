**APPROVED** — one Minor finding (mutable setters on `ArticleKnowledgeChunk`), which the arch review explicitly cleared as acceptable. No blockers.

Let me write the output artifact:

---

# Implementation: Decouple Article GatherContextStep from KnowledgeBase Internals

## What was implemented
Introduced `IArticleKnowledgeSource` as a consumer-owned contract in the Article module, implemented by `KnowledgeBaseArticleKnowledgeSource` adapter in the KnowledgeBase module. `GatherContextStep` now calls the contract instead of dispatching `SearchDocumentsRequest` via `IMediator` directly. The three Article allowlist entries in `ModuleBoundariesTests` were deleted, making the existing architecture test enforce the boundary automatically.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Article/Contracts/IArticleKnowledgeSource.cs` — new contract interface + `ArticleKnowledgeChunk` DTO
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/KnowledgeBaseArticleKnowledgeSource.cs` — new `internal sealed` adapter delegating to `IMediator.Send(SearchDocumentsRequest)`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs` — added `services.AddScoped<IArticleKnowledgeSource, KnowledgeBaseArticleKnowledgeSource>()` after the existing `IArticleStyleGuideSource` registration
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/GatherContextStep.cs` — removed `IMediator` dependency, removed KB `using` statement, added `IArticleKnowledgeSource _knowledgeSource`, rewrote `GatherKnowledgeBaseSnippetsAsync`
- `backend/test/Anela.Heblo.Tests/Article/Pipeline/GatherContextStepTests.cs` — replaced `Mock<IMediator>` KB setups with `Mock<IArticleKnowledgeSource>`
- `backend/test/Anela.Heblo.Tests/KnowledgeBase/Infrastructure/KnowledgeBaseArticleKnowledgeSourceTests.cs` — new: 4 tests for adapter (request dispatch, field projection, empty result, CancellationToken propagation)
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — deleted three Article allowlist entries; `ArticleAllowlist` is now empty
- `backend/test/Anela.Heblo.Tests/Article/Pipeline/GenerateArticleJobTests.cs` — updated `GatherContextStep` constructor call to pass `IArticleKnowledgeSource`
- `backend/test/Anela.Heblo.Tests/Article/UseCases/SourceEnrichmentIntegrationTests.cs` — updated mediator KB stub to `IArticleKnowledgeSource.SearchAsync` stub

## Tests
- `GatherContextStepTests.cs` — 6 tests: KB path, web-disabled, KB-throws, web-throws, URL dedup, style guide
- `KnowledgeBaseArticleKnowledgeSourceTests.cs` — 4 new tests: correct request params, 4-field projection, empty result, CancellationToken forwarding
- `ModuleBoundariesTests.cs` — existing theory now enforces Article→KnowledgeBase boundary with empty allowlist (was 3 exceptions)
- All 25 affected tests pass

## How to verify
```bash
cd backend
dotnet build test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-incremental -q 2>&1 | grep "error CS"   # should be empty
dotnet format src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --verify-no-changes              # passes
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ModuleBoundariesTests|FullyQualifiedName~GatherContextStepTests|FullyQualifiedName~KnowledgeBaseArticleKnowledgeSourceTests|FullyQualifiedName~GenerateArticleJobTests|FullyQualifiedName~SourceEnrichmentIntegrationTests"
# Expected: Passed 25, Failed 0
```

## Notes
- `ArticleKnowledgeChunk` uses mutable `{ get; set; }` properties per spec/arch-review guidance ("implementer's choice, both styles safe"). Minor quality note flagged by final reviewer — consistent with existing `ChunkResult` pattern; a follow-up can migrate both to `init`.
- Two additional test files (`GenerateArticleJobTests`, `SourceEnrichmentIntegrationTests`) not listed in the original task plan also needed updating — they constructed `GatherContextStep` with the old `(IMediator, ...)` signature. Fixed as part of validation.
- The pre-existing `GenerateTargetFrameworkMonikerAttribute` MSBuild error in `Anela.Heblo.Xcc` is a worktree artifact unrelated to this change; it exists on the base branch before any commits.

## PR Summary

Decouples `GatherContextStep` from KnowledgeBase internals by introducing `IArticleKnowledgeSource`, a consumer-owned contract in the Article module, following the established `IArticleStyleGuideSource` pattern. The KnowledgeBase module provides the adapter (`KnowledgeBaseArticleKnowledgeSource`, `internal sealed`) that bridges to the existing `SearchDocumentsRequest` handler. `IMediator` is removed from `GatherContextStep`'s constructor. The three Article allowlist entries in `ModuleBoundariesTests` are deleted — the existing architecture test now enforces the boundary automatically.

### Changes
- `Features/Article/Contracts/IArticleKnowledgeSource.cs` — new contract interface + ArticleKnowledgeChunk DTO
- `Features/KnowledgeBase/Infrastructure/KnowledgeBaseArticleKnowledgeSource.cs` — new internal sealed adapter
- `Features/KnowledgeBase/KnowledgeBaseModule.cs` — DI registration for new contract binding
- `Features/Article/UseCases/Generate/Pipeline/GatherContextStep.cs` — consumes IArticleKnowledgeSource, drops IMediator
- `test/…/GatherContextStepTests.cs` — KB mock switched to IArticleKnowledgeSource
- `test/…/KnowledgeBaseArticleKnowledgeSourceTests.cs` — new: 4 adapter unit tests
- `test/…/ModuleBoundariesTests.cs` — ArticleAllowlist emptied (3 entries removed)
- `test/…/GenerateArticleJobTests.cs` and `SourceEnrichmentIntegrationTests.cs` — constructor call updated

## Status
DONE