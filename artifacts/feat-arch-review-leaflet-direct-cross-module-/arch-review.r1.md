# Architecture Review: Decouple Leaflet Module from KnowledgeBase Domain

## Skip Design: true

Backend-only refactor: new interface + adapter + enum relocation + reflection test. No UI components, screens, or visual changes. OpenAPI surface unchanged, so no TypeScript regeneration either.

## Architectural Fit Assessment

The spec's intent — Leaflet declares a narrow consumer-owned interface, KnowledgeBase implements it, DI is registered by the provider — is a textbook Dependency Inversion application that aligns cleanly with the project's "communication only through contracts/interfaces" rule (`docs/architecture/development_guidelines.md:46-50`) and the existing `Contracts/` convention used by ~20 other feature modules.

**One concrete conflict with the spec needs correction before implementation begins:** FR-4 proposes moving `DocumentType` to `Anela.Heblo.Application.Shared.Rag`. That will not compile.

- `KnowledgeBaseDocument` and `KnowledgeBaseChunk` (in `Anela.Heblo.Domain/Features/KnowledgeBase/`) hold a `DocumentType` property each. They are **Domain** types.
- `Anela.Heblo.Application.csproj` references `Anela.Heblo.Domain`. The reverse reference does not exist and must not exist (Clean Architecture inverts that direction).
- Therefore Domain entities cannot import `Anela.Heblo.Application.Shared.Rag.DocumentType`.

The existing arrangement is already broken in the *opposite* direction: `Application/Shared/Rag/OneDriveFolderMapping.cs:1` currently imports `Anela.Heblo.Domain.Features.KnowledgeBase` to reach `DocumentType` — which is a Domain → Domain feature module reference smuggled through the shared RAG namespace. Both problems vanish if `DocumentType` lives in the **Domain shared** layer, which already exists at `backend/src/Anela.Heblo.Domain/Shared/` (currently hosts `CurrencyCode.cs`, `Result.cs`) and is documented as "Cross-cutting domain utilities" (`docs/architecture/filesystem.md:25`).

Other than that correction, the spec is implementable as written. Integration points are all internal: DI composition (`KnowledgeBaseModule.cs`), `GenerateLeafletHandler` constructor, `LeafletIngestionJob.cs:62`, four enum-consuming files in Domain/Persistence/Application, and the test project.

## Proposed Architecture

### Component Overview

```
┌──────────────────────────────────────┐         ┌──────────────────────────────────────┐
│  Anela.Heblo.Application             │         │  Anela.Heblo.Domain                  │
│                                      │         │                                      │
│  Features/Leaflet/                   │         │  Features/KnowledgeBase/             │
│   Contracts/                         │         │   IKnowledgeBaseRepository           │
│    ILeafletKnowledgeSource ─────────────┐      │   KnowledgeBaseChunk                 │
│    KnowledgeSearchResult         │   │  │      │   KnowledgeBaseDocument              │
│   UseCases/GenerateLeaflet/      │   │  │      │     │                                │
│    GenerateLeafletHandler ───────┘   │  │      │     │ (uses)                         │
│                                      │  │      │     ▼                                │
│  Features/KnowledgeBase/             │  │      │  Shared/                             │
│   Infrastructure/                    │  │      │   DocumentType  ◄─── single source   │
│    KnowledgeBaseLeafletSourceAdapter │  │      │                     of truth         │
│      implements ILeafletKnowledgeSource │      └──────────────────────────────────────┘
│      delegates to IKnowledgeBaseRepository       ▲          ▲           ▲
│                                      │  │        │          │           │
│  Shared/Rag/                         │  │        │          │           │
│   OneDriveFolderMapping ─────────────┼──┼────────┘          │           │
│   RagFeatureOptions                  │  │                   │           │
│                                      │  │                   │           │
│  Features/Leaflet/                   │  │                   │           │
│   Infrastructure/Jobs/               │  │                   │           │
│    LeafletIngestionJob ──────────────┼──┼───────────────────┘           │
│                                      │  │                               │
└──────────────────────────────────────┘  │                               │
                                          │                               │
  ┌───────────────────────────────────────┘                               │
  │ (DI: KnowledgeBaseModule registers ILeafletKnowledgeSource)           │
  │                                                                       │
  │  Anela.Heblo.Persistence/KnowledgeBase/                               │
  │   KnowledgeBaseRepository, KnowledgeBaseChunkConfiguration ───────────┘
  │   KnowledgeBaseDocumentConfiguration  (all read DocumentType from Domain/Shared)
```

Dependency arrows go **down only**: Application → Domain. KnowledgeBase domain → DocumentType. Leaflet handler → Leaflet contract → (resolved via DI) → KnowledgeBase adapter → KnowledgeBase repository → Domain chunk → DocumentType.

### Key Design Decisions

#### Decision 1: Where `DocumentType` lives
**Options considered:**
- A. `Anela.Heblo.Application.Shared.Rag` (as the spec proposes).
- B. `Anela.Heblo.Domain.Shared` (or `Anela.Heblo.Domain.Shared.Rag`).
- C. Leave in `Anela.Heblo.Domain.Features.KnowledgeBase`, accept it as the canonical cross-module enum.
- D. Duplicate the enum per module with explicit mapping.

**Chosen approach:** **B.** Create `Anela.Heblo.Domain/Shared/Rag/DocumentType.cs` with namespace `Anela.Heblo.Domain.Shared.Rag`.

**Rationale:** Option A does not compile — Domain entities cannot import Application. Option C does not fix the spec's central concern (cross-module domain reference from Leaflet). Option D introduces mapping risk for an enum persisted as an integer column. Option B is the only choice that lets all current consumers (Domain entities, EF configurations, Application shared, Leaflet job) reference the same type without violating layering. The `Domain/Shared/` folder already exists and is documented for "cross-cutting domain utilities" — `DocumentType` fits exactly. A subfolder `Rag/` keeps it grouped with future shared RAG domain types.

#### Decision 2: Shape of `ILeafletKnowledgeSource`
**Options considered:**
- A. Return `(KnowledgeBaseChunk, double)` tuples (current shape) — requires Leaflet to keep referencing the Domain chunk type.
- B. Return a Leaflet-owned DTO `KnowledgeSearchResult` exposing just the fields the handler reads (`Content`, `Score`).
- C. Return a shared RAG type in `Application/Shared/Rag/`.

**Chosen approach:** **B.** `KnowledgeSearchResult` is a Leaflet-owned class (per project rule "DTOs are classes, never C# records" — CLAUDE.md) in `Application/Features/Leaflet/Contracts/`.

**Rationale:** Option A would defeat the whole exercise — the handler would still reference `KnowledgeBaseChunk`. Option C creates a new ambiguous coupling for what is currently a single-consumer concern. Option B is the minimum surface required (`GenerateLeafletHandler.cs:55-57,93` reads `Score` and `Chunk.Content` — nothing else) and follows YAGNI. The spec's "conditional" framing for this DTO is misleading; inspection of `IKnowledgeBaseRepository.SearchSimilarAsync` confirms the return type *is* a Domain tuple, so the DTO is **mandatory**, not conditional.

#### Decision 3: Adapter location and DI ownership
**Options considered:**
- A. Adapter in `Application/Features/KnowledgeBase/Infrastructure/` (provider owns).
- B. Adapter in `Application/Features/Leaflet/Infrastructure/` (consumer owns).
- C. Adapter in `Application/Shared/Rag/`.

**Chosen approach:** **A.** The adapter `KnowledgeBaseLeafletSourceAdapter` lives in `Application/Features/KnowledgeBase/Infrastructure/`, and `KnowledgeBaseModule.AddKnowledgeBaseModule` registers `ILeafletKnowledgeSource → KnowledgeBaseLeafletSourceAdapter`.

**Rationale:** This keeps the dependency direction inverted properly: KnowledgeBase depends on Leaflet's contract (one tiny interface), Leaflet does not depend on KnowledgeBase at all. The Leaflet module remains buildable and testable without referencing anything in `KnowledgeBase`. The `KnowledgeBase` module already references `Application/Features/Leaflet/UseCases/IndexLeaflet` from elsewhere (Pipeline references), so the build graph is already permissive in that direction.

#### Decision 4: Architecture enforcement test scope
**Options considered:**
- A. Test only Leaflet → KnowledgeBase domain (matches FR-5 verbatim).
- B. Test Leaflet → any KnowledgeBase namespace (Domain, Application.Features.KnowledgeBase, Persistence.KnowledgeBase).
- C. Generalize to "any module's types must not reference another module's domain or feature internals".

**Chosen approach:** **B**, scoped to the current concern.

**Rationale:** A is too narrow — Leaflet could regress by importing from `Application.Features.KnowledgeBase` (e.g. the new adapter) instead of Domain, and the test would not catch it. C is out of scope and explicitly deferred by the spec. B catches the realistic regression set without scope creep. The adapter type itself is allow-listable if needed (it never should be — Leaflet only sees the interface).

## Implementation Guidance

### Directory / Module Structure

New files:
```
backend/src/Anela.Heblo.Domain/Shared/Rag/
└── DocumentType.cs                                 # MOVED from Features/KnowledgeBase/KnowledgeBaseDocument.cs

backend/src/Anela.Heblo.Application/Features/Leaflet/Contracts/
├── ILeafletKnowledgeSource.cs                      # NEW (consumer-owned interface)
└── KnowledgeSearchResult.cs                        # NEW (Leaflet-owned DTO, class)

backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/
└── KnowledgeBaseLeafletSourceAdapter.cs            # NEW (provider-owned adapter)

backend/test/Anela.Heblo.Tests/Architecture/
└── ModuleBoundariesTests.cs                        # NEW (reflection-based boundary test)
```

Modified files (import paths and types only; no behavior change):
```
backend/src/Anela.Heblo.Domain/Features/KnowledgeBase/
├── KnowledgeBaseDocument.cs                        # Drop DocumentType enum body; add using Anela.Heblo.Domain.Shared.Rag
└── KnowledgeBaseChunk.cs                           # add using Anela.Heblo.Domain.Shared.Rag

backend/src/Anela.Heblo.Application/Shared/Rag/
└── OneDriveFolderMapping.cs                        # Replace using Domain.Features.KnowledgeBase with using Domain.Shared.Rag

backend/src/Anela.Heblo.Application/Features/Leaflet/
├── UseCases/GenerateLeaflet/GenerateLeafletHandler.cs  # Swap IKnowledgeBaseRepository → ILeafletKnowledgeSource
└── Infrastructure/Jobs/LeafletIngestionJob.cs          # Replace using Domain.Features.KnowledgeBase with using Domain.Shared.Rag

backend/src/Anela.Heblo.Application/Features/KnowledgeBase/
├── KnowledgeBaseModule.cs                              # Register ILeafletKnowledgeSource → adapter
├── Services/ConversationIndexingStrategy.cs            # update using
├── Services/KnowledgeBaseDocIndexingStrategy.cs        # update using
├── Services/DocumentIndexingService.cs                 # update using
├── Services/IIndexingStrategy.cs                       # update using
├── UseCases/UploadDocument/{Handler,Request}.cs        # update using
├── UseCases/IndexDocument/{Handler,Request}.cs         # update using
├── UseCases/GetChunkDetail/{Handler,Request}.cs        # update using
└── Infrastructure/Jobs/KnowledgeBaseIngestionJob.cs    # update using

backend/src/Anela.Heblo.Persistence/KnowledgeBase/
├── KnowledgeBaseChunkConfiguration.cs               # update using (KnowledgeBaseChunkConfiguration.cs:22-25 reads DocumentType)
├── KnowledgeBaseDocumentConfiguration.cs            # update using
└── KnowledgeBaseRepository.cs                       # update using

backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs   # update using
backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletChunkDetail/*.cs
... and all other Leaflet handlers/requests currently importing DocumentType (verify via reference search)

backend/src/Anela.Heblo.Persistence/Migrations/*.cs                  # update using (Designer files reference DocumentType in default-value HasDefaultValue snapshots)
```

**Important:** Migration `*.Designer.cs` and `ApplicationDbContextModelSnapshot.cs` files are regenerated by EF Core. Hand-editing the `using` directives is acceptable as a one-shot rewrite, but the implementer must run `dotnet ef migrations script --idempotent` (or equivalent) on a scratch DB to confirm no migration is needed and no snapshot drift occurs.

### Interfaces and Contracts

```csharp
// backend/src/Anela.Heblo.Domain/Shared/Rag/DocumentType.cs
namespace Anela.Heblo.Domain.Shared.Rag;

public enum DocumentType
{
    KnowledgeBase = 0,
    Conversation  = 1,
    Leaflet       = 2,
    Article       = 3,
}
```
Underlying integer values are **identical** to the current declaration in `KnowledgeBaseDocument.cs:27-33`. No EF migration, no data backfill, no enum-to-int recalibration.

```csharp
// backend/src/Anela.Heblo.Application/Features/Leaflet/Contracts/ILeafletKnowledgeSource.cs
namespace Anela.Heblo.Application.Features.Leaflet.Contracts;

/// <summary>
/// Leaflet-owned read-only abstraction over the knowledge base vector index.
/// Implemented by the KnowledgeBase module via an adapter.
/// </summary>
public interface ILeafletKnowledgeSource
{
    Task<IReadOnlyList<KnowledgeSearchResult>> SearchSimilarAsync(
        float[] queryEmbedding,
        int topK,
        CancellationToken cancellationToken);
}
```

```csharp
// backend/src/Anela.Heblo.Application/Features/Leaflet/Contracts/KnowledgeSearchResult.cs
namespace Anela.Heblo.Application.Features.Leaflet.Contracts;

public class KnowledgeSearchResult
{
    public string Content { get; set; } = string.Empty;
    public double Score { get; set; }
}
```
This is the **minimum** surface the handler consumes (`GenerateLeafletHandler.cs:55-57` filters by `Score`, line 93 reads `Chunk.Content`). No `Id`, no `DocumentType`, no embedding — Leaflet does not need them. If a future handler needs more, extend then.

```csharp
// backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/KnowledgeBaseLeafletSourceAdapter.cs
namespace Anela.Heblo.Application.Features.KnowledgeBase.Infrastructure;

internal sealed class KnowledgeBaseLeafletSourceAdapter : ILeafletKnowledgeSource
{
    private readonly IKnowledgeBaseRepository _repository;

    public KnowledgeBaseLeafletSourceAdapter(IKnowledgeBaseRepository repository) =>
        _repository = repository;

    public async Task<IReadOnlyList<KnowledgeSearchResult>> SearchSimilarAsync(
        float[] queryEmbedding,
        int topK,
        CancellationToken cancellationToken)
    {
        var hits = await _repository.SearchSimilarAsync(queryEmbedding, topK, cancellationToken);
        return hits
            .Select(h => new KnowledgeSearchResult { Content = h.Chunk.Content, Score = h.Score })
            .ToList();
    }
}
```
Adapter is `internal` because nothing outside the assembly should construct it — DI resolves through the interface only. `Anela.Heblo.Tests` already has `InternalsVisibleTo`, so unit tests still see it.

DI registration in `KnowledgeBaseModule.cs` (one line addition):
```csharp
services.AddScoped<ILeafletKnowledgeSource, KnowledgeBaseLeafletSourceAdapter>();
```

### Data Flow

**GenerateLeaflet request:**
1. Controller → MediatR → `GenerateLeafletHandler`.
2. Handler calls `_kbSource.SearchSimilarAsync(topicVector, _options.KbTopK, ct)` — typed as `ILeafletKnowledgeSource`.
3. DI resolves to `KnowledgeBaseLeafletSourceAdapter` (registered by KnowledgeBaseModule).
4. Adapter forwards to `IKnowledgeBaseRepository.SearchSimilarAsync`, gets `List<(KnowledgeBaseChunk, double)>`.
5. Adapter projects to `List<KnowledgeSearchResult>`.
6. Handler filters `hits.Where(x => x.Score >= _options.MinSimilarityScore)`, reads `h.Content` — identical semantics.

**Leaflet ingestion:**
1. `LeafletIngestionJob.ExecuteAsync` reads `_options.OneDriveFolderMappings`, filters `m.DocumentType == DocumentType.Leaflet` — where `DocumentType` is now imported from `Anela.Heblo.Domain.Shared.Rag`.
2. Same `OneDriveFolderMapping` items, same `DocumentType.Leaflet` integer value (2), same filter result.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Spec proposes `DocumentType` in `Application/Shared/Rag/` — won't compile (Domain entities can't reference Application). | **CRITICAL** | Amend spec FR-4 to `Domain/Shared/Rag/`. Verified `Anela.Heblo.Domain.csproj` has no reference to Application. |
| Migration `*.Designer.cs` / `ApplicationDbContextModelSnapshot.cs` reference `DocumentType` and break on namespace change. | HIGH | These are auto-generated EF artifacts. After updating `using` directives, run a no-op `dotnet ef migrations add Verify --no-build` against a scratch DB to confirm no model drift; delete that scratch migration. Do **not** create a real migration unless the snapshot diff is non-trivial. |
| Existing unit tests for `GenerateLeafletHandler` mock `IKnowledgeBaseRepository`. | MEDIUM | Update tests to mock `ILeafletKnowledgeSource` returning `KnowledgeSearchResult` lists. The mock surface shrinks — easier, not harder. |
| Cyclic build graph: KnowledgeBase adapter references Leaflet's `Contracts/` namespace; both live in the same `Anela.Heblo.Application` assembly. | LOW | Single-assembly references are namespace-only, no project cycle. C# allows it. The boundary test (FR-5) is the only enforcement. |
| DI registration order: Leaflet's `GenerateLeafletHandler` resolves `ILeafletKnowledgeSource` at request time, but only after `KnowledgeBaseModule.AddKnowledgeBaseModule` has registered the adapter. | LOW | Inspect `Anela.Heblo.API/Program.cs` (or `ApplicationModule.cs`) and confirm `AddKnowledgeBaseModule` runs before `AddLeafletModule` is resolved. DI registration is order-insensitive (all registrations complete before any service resolves), so this is safe by construction. Worth a one-line comment in `KnowledgeBaseModule.cs` near the registration. |
| Adapter introduces an extra projection allocation per search call. | LOW | `SearchSimilarAsync` returns at most `KbTopK = 8` items per call. Allocation cost is dominated by the vector search itself by orders of magnitude. NFR-1 ±5% is comfortably met. |
| `ApplicationDbContextModelSnapshot.cs` is shared across all migrations — diff noise on the `using` change. | LOW | Acceptable. One-line `using` edit in a generated file. |
| Other consumers of `DocumentType` (e.g. `KnowledgeBaseController.cs`, several `UploadDocument`/`IndexDocument` files) miss the import rename. | MEDIUM | `dotnet build` catches all of them. The implementer should grep `using Anela.Heblo.Domain.Features.KnowledgeBase;` across the solution after the move and verify each survivor still needs the import (some only needed it for `DocumentType`; others legitimately need `KnowledgeBaseDocument` etc.). |
| Architecture test (FR-5) cannot detect references introduced through generic constraints or attribute arguments. | LOW | Document the known limitation in the test file. Constructor params, fields, properties, method params/returns, generic args, attribute types — the listed scope — covers the realistic regression set. |
| Future modules may try to import `DocumentType` from its old location after the move. | LOW | Compile error. Old namespace gone. |

## Specification Amendments

The spec is marked `Status: COMPLETE` with no open questions, but one functional requirement is incorrect and needs revision before implementation:

1. **FR-4 location (mandatory change):** Replace every occurrence of `Anela.Heblo.Application.Shared.Rag` as `DocumentType`'s new home with `Anela.Heblo.Domain.Shared.Rag`. New file path: `backend/src/Anela.Heblo.Domain/Shared/Rag/DocumentType.cs`. Update FR-4 acceptance criteria's namespace bullet accordingly. All "known consumers" listed in FR-4 still apply; they import from the new Domain namespace instead.

2. **FR-1 / Data Model — `KnowledgeSearchResult` is not conditional:** The current `IKnowledgeBaseRepository.SearchSimilarAsync` returns `List<(KnowledgeBaseChunk Chunk, double Score)>` (Domain type). Therefore a Leaflet-owned DTO **must** be introduced. Drop the "if … no new DTO is needed" language.

3. **FR-6 — Documentation:** Tighten the doc-update wording: `docs/architecture/development_guidelines.md` should illustrate the "consumer-defines-contract, provider-implements-adapter, provider-registers-DI" pattern with the Leaflet/KnowledgeBase pair as the example. `docs/architecture/filesystem.md` should document `Domain/Shared/Rag/` (not `Application/Shared/Rag/`) as the canonical home for cross-module RAG domain concepts, and keep `Application/Shared/Rag/` as the home for cross-module RAG **application/infrastructure** types (`OneDriveFolderMapping`, `RagFeatureOptions`, `IRagQueryExpander`).

4. **FR-5 scope (clarification, not behavior change):** Change "asserts that none of their referenced types live in the `Anela.Heblo.Domain.Features.KnowledgeBase` namespace" to "…in any namespace starting with `Anela.Heblo.Domain.Features.KnowledgeBase`, `Anela.Heblo.Application.Features.KnowledgeBase`, or `Anela.Heblo.Persistence.KnowledgeBase`." This catches the realistic regression set (e.g. Leaflet starts importing the adapter type directly).

5. **NFR-3 — Backwards compatibility:** Reaffirm: because the underlying integer values of `DocumentType` are preserved exactly and the EF Core column type is unchanged (`HasConversion<int>()` in `KnowledgeBaseChunkConfiguration.cs:25`), no migration is needed. Adding "Verified against `ApplicationDbContextModelSnapshot.cs` post-change — snapshot must show namespace-only diff, no schema diff" as an acceptance check would close the loop.

## Prerequisites

None. Everything required is already in the repo:

- `Anela.Heblo.Domain/Shared/` folder exists (`CurrencyCode.cs`, `Result.cs`).
- `Application/Features/Leaflet/` has no `Contracts/` subfolder yet — create on first commit; matches the project convention used by all complex feature modules (`docs/architecture/filesystem.md:111`).
- `Application/Features/KnowledgeBase/Infrastructure/` exists.
- `backend/test/Anela.Heblo.Tests/` exists and is wired into the standard `dotnet test` run; new `Architecture/` subfolder is a single-directory creation. `ReflectionValidationTests.cs` is the precedent pattern.
- No new NuGet packages, no Roslyn analyzers, no project-reference restrictions, no EF migrations, no Docker changes, no frontend changes, no OpenAPI regeneration.

Implementation order recommended:
1. Move `DocumentType` to `Domain/Shared/Rag/`; fix every `using`; `dotnet build` (catches every miss).
2. Add `ILeafletKnowledgeSource` + `KnowledgeSearchResult` in `Application/Features/Leaflet/Contracts/`.
3. Add `KnowledgeBaseLeafletSourceAdapter` in `Application/Features/KnowledgeBase/Infrastructure/`; register in `KnowledgeBaseModule.cs`.
4. Swap `GenerateLeafletHandler` constructor parameter; update its unit tests.
5. Add `Architecture/ModuleBoundariesTests.cs`; verify it fails for a sample violation, then passes on current state.
6. Update docs (`development_guidelines.md`, `filesystem.md`).
7. `dotnet build` + `dotnet format` + `dotnet test`.