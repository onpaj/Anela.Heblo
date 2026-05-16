# Implementation: Decouple Leaflet Module from KnowledgeBase Domain

## What was implemented

The Leaflet module no longer depends directly on `IKnowledgeBaseRepository` or the `DocumentType` enum from `Anela.Heblo.Domain.Features.KnowledgeBase`. All cross-module communication now goes through a Leaflet-owned contract implemented by a KnowledgeBase-owned adapter. A reflection-based CI test enforces the boundary permanently.

## Files created/modified

**New files:**
- `backend/src/Anela.Heblo.Domain/Shared/Rag/DocumentType.cs` — enum relocated from KnowledgeBase domain; single source of truth for all modules
- `backend/src/Anela.Heblo.Application/Features/Leaflet/Contracts/ILeafletKnowledgeSource.cs` — Leaflet-owned interface for vector search
- `backend/src/Anela.Heblo.Application/Features/Leaflet/Contracts/KnowledgeSearchResult.cs` — Leaflet-owned DTO (class, `init` setters) with `Content` and `Score`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/KnowledgeBaseLeafletSourceAdapter.cs` — thin adapter delegating to `IKnowledgeBaseRepository`, projecting `(KnowledgeBaseChunk, double)` → `KnowledgeSearchResult`
- `backend/test/Anela.Heblo.Tests/Features/KnowledgeBase/Infrastructure/KnowledgeBaseLeafletSourceAdapterTests.cs` — 3 unit tests (forwarding, projection, empty)
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — reflection test asserting no Leaflet type references any of `Domain.Features.KnowledgeBase`, `Application.Features.KnowledgeBase`, or `Persistence.KnowledgeBase`

**Modified files:**
- `backend/src/Anela.Heblo.Domain/Features/KnowledgeBase/KnowledgeBaseDocument.cs` — `DocumentType` enum body removed
- `~20 files across Domain/Application/Persistence/API` — `using` directives updated to import `DocumentType` from `Anela.Heblo.Domain.Shared.Rag`
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletHandler.cs` — constructor/field swapped to `ILeafletKnowledgeSource`, `h.Chunk.Content` → `h.Content` for KB hits
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs` — registers `ILeafletKnowledgeSource → KnowledgeBaseLeafletSourceAdapter`
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/GenerateLeafletHandlerTests.cs` — mock type swapped to `ILeafletKnowledgeSource`
- `docs/architecture/development_guidelines.md` — consumer-owns-contract pattern documented with concrete example
- `docs/architecture/filesystem.md` — `Domain/Shared/Rag/` and `Application/Shared/Rag/` documented

## Tests

- `KnowledgeBaseLeafletSourceAdapterTests` — 3 tests (forwarding, projection, empty result)
- `GenerateLeafletHandlerTests` — 15 tests updated (mock type changed to `ILeafletKnowledgeSource`)
- `ModuleBoundariesTests` — 1 reflection test, sanity-checked by introducing/reverting a deliberate violation
- Full suite: **3149 passed, 3 skipped (pre-existing infrastructure tests), 0 failed**

## How to verify

```bash
# From repo root
dotnet build Anela.Heblo.sln
dotnet format Anela.Heblo.sln --verify-no-changes
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --nologo
# Confirm no Leaflet→KnowledgeBase domain references remain:
grep -r "Anela.Heblo.Domain.Features.KnowledgeBase" backend/src/Anela.Heblo.Application/Features/Leaflet/
```

## Notes

- `DocumentType` integer values (KnowledgeBase=0, Conversation=1, Leaflet=2, Article=3) are preserved exactly — no EF migration needed
- `LeafletIngestionJob` still depends on `IOneDriveService` and `OneDriveFile` from `Application.Features.KnowledgeBase.Services`; these are pre-existing out-of-scope violations documented in the boundary test allowlist
- `UploadLeafletHandler` and `IndexLeafletHandler` depend on `IDocumentTextExtractor` from the same KnowledgeBase services namespace — also in the allowlist with justification
- The architecture reviewer's amendment to FR-4 (use `Domain/Shared/Rag/` not `Application/Shared/Rag/`) was applied — Domain entities cannot import from Application

## PR Summary

Introduces the consumer-owns-contract pattern to break a direct cross-module coupling from Leaflet → KnowledgeBase domain. `GenerateLeafletHandler` now resolves knowledge-base search through `ILeafletKnowledgeSource` (Leaflet-owned), implemented transparently by `KnowledgeBaseLeafletSourceAdapter` (KnowledgeBase-owned). The `DocumentType` enum, which was co-owned in practice, is relocated to `Domain/Shared/Rag/` — the only location accessible to both Domain entities and Application modules without violating Clean Architecture layers. A reflection-based test in `Architecture/ModuleBoundariesTests.cs` enforces the boundary in CI; violations fail with a message naming the offending type and member.

No runtime behavior changes, no HTTP API changes, no database schema changes.

### Changes
- `Domain/Shared/Rag/DocumentType.cs` — moved enum; integer values preserved
- `Application/Features/Leaflet/Contracts/` — new `ILeafletKnowledgeSource` + `KnowledgeSearchResult`
- `Application/Features/KnowledgeBase/Infrastructure/KnowledgeBaseLeafletSourceAdapter.cs` — new thin adapter
- `Application/Features/KnowledgeBase/KnowledgeBaseModule.cs` — DI registration for the adapter
- `Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletHandler.cs` — constructor swap
- `test/Architecture/ModuleBoundariesTests.cs` — new boundary enforcement test
- `~20 source files` — `using` directive updates for `DocumentType` relocation
- `docs/architecture/development_guidelines.md` + `filesystem.md` — pattern and namespace documented

## Status
DONE