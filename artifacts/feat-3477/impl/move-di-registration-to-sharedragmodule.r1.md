# Implementation: move-di-registration-to-sharedragmodule

## What was implemented
Changed `AddSharedRagModule` to accept an `IConfiguration` parameter and own all `IDocumentTextExtractor`/`IOneDriveService` DI registrations (including the Graph-vs-Mock selection logic, moved verbatim). Stripped the equivalent registrations out of `KnowledgeBaseModule.cs`. Updated the single `ApplicationModule.cs` call site to pass `configuration`.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Shared/Rag/SharedRagModule.cs` — `AddSharedRagModule` now takes `IConfiguration`; registers the 3 `IDocumentTextExtractor` implementations and the Graph-vs-Mock `IOneDriveService` selection (moved verbatim from `KnowledgeBaseModule`, including the comment about the pre-existing "KnowledgeBase"-only config-section gap, per NFR-1/arch-review Decision 3 — zero behavioral change)
- `backend/src/Anela.Heblo.Application/ApplicationModule.cs` — call site updated to `services.AddSharedRagModule(configuration);`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs` — removed the 3 `IDocumentTextExtractor` registrations and the OneDrive Graph/Mock selection block (including `AddHttpClient`/`AddMemoryCache`); removed the now-unused `using Anela.Heblo.Application.Shared.Rag;` / `.DocumentExtractors` / `.OneDrive` lines (confirmed via grep no remaining references to the relocated types in this file); kept `KnowledgeBaseOptions` binding and all other registrations (`ChatTranscriptPreprocessor`, `IChunkSummarizer`, `IConversationTopicSummarizer`, `IIndexingStrategy` x2, `IDocumentIndexingService`, the Leaflet/Article adapter bindings, `IKnowledgeBaseRepository`, `IProductEnrichmentCache`, `QuestionLoggingBehavior`) untouched

## Tests
- `dotnet build Anela.Heblo.sln` — **Build succeeded, 0 errors** (254 pre-existing nullable-reference warnings in unrelated test files, none new/related to this change)
- `dotnet test ... --filter "FullyQualifiedName~ApplicationStartupTests"` — **Passed! Failed: 0, Passed: 349, Skipped: 0, Total: 349** (confirms `IDocumentTextExtractor`/`IOneDriveService` still resolve via DI for both KnowledgeBase and Leaflet, and every controller resolves — the FR-4 acceptance criterion)
- `dotnet test ... --filter "FullyQualifiedName~Shared.Rag|FullyQualifiedName~KnowledgeBase|FullyQualifiedName~Leaflet"` — 378 passed, 3 skipped, **26 failed** — all 26 failures are pre-existing `*RepositoryIntegrationTests` (Testcontainers/PostgreSQL) failing with `Docker is either not running or misconfigured` — a sandbox environment limitation with no Docker daemon, unrelated to this change (same root cause across `KnowledgeBaseRepositoryIntegrationTests` and `LeafletRepositoryIntegrationTests`, none touching the relocated types).

## How to verify
1. `dotnet build Anela.Heblo.sln` — 0 errors.
2. `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ApplicationStartupTests"` — 349/349 pass.

## Notes
No functional deviation from the plan. Build/test commands run from the worktree root, consistent with tasks 1 and 2.

## Status
DONE
