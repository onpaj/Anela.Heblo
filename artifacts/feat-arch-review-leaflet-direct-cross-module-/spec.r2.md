# Specification: Decouple Leaflet Module from KnowledgeBase Domain

## Summary
The Leaflet module currently imports types directly from the KnowledgeBase domain layer, violating the project's "no direct cross-module references" rule. This spec defines a refactor that introduces a Leaflet-owned abstraction for vector search and relocates the shared `DocumentType` enum to the existing `Application/Shared/Rag` location, restoring module independence without changing any runtime behavior or database schema.

## Background
`docs/architecture/development_guidelines.md` mandates that feature modules communicate only through contracts/interfaces, never through direct domain references. The daily architecture review on 2026-05-14 identified two violations in the Leaflet module:

1. **`GenerateLeafletHandler.cs`** depends on `IKnowledgeBaseRepository` from `Anela.Heblo.Domain.Features.KnowledgeBase` to perform `SearchSimilarAsync` vector lookups.
2. **`LeafletIngestionJob.cs`** depends on the `DocumentType` enum from the same KnowledgeBase domain namespace to filter `OneDriveFolderMappings`.

Investigation during specification revealed that `DocumentType` is already a cross-module concept in practice — its four members (`KnowledgeBase`, `Conversation`, `Leaflet`, `Article`) span four different feature modules, and the shared infrastructure type `OneDriveFolderMapping` (in `Application/Shared/Rag/`) already takes a hard dependency on it. The correct fix therefore is two-pronged: a Leaflet-owned interface for the search behavior, and relocation of `DocumentType` into the same shared RAG infrastructure namespace that already hosts `OneDriveFolderMapping`, `RagFeatureOptions`, and `RagQueryExpansionConfig`.

`OneDriveFolderMappings` itself is **not** a KnowledgeBase-owned type. It is a collection property on the shared `RagFeatureOptions` base class, which `LeafletOptions` inherits from. Each module binds its own options to its own configuration section, so the Leaflet job continues to read `LeafletOptions.OneDriveFolderMappings` directly with no cross-module reference once the enum is relocated.

## Functional Requirements

### FR-1: Introduce Leaflet-owned vector search contract
Define a new interface `ILeafletKnowledgeSource` in the Leaflet feature's contract location (`backend/src/Anela.Heblo.Application/Features/Leaflet/Contracts/`, or the closest existing pattern in that module). The interface must expose **only** the operations Leaflet actually consumes — currently `SearchSimilarAsync` with the exact parameter/return signature already used by `GenerateLeafletHandler`.

**Acceptance criteria:**
- `ILeafletKnowledgeSource` is declared inside the Leaflet module's namespace, not in KnowledgeBase.
- The interface surface is the minimum required by `GenerateLeafletHandler` — no additional methods carried over for speculative future use.
- The method signature (parameters, return type, async behavior, cancellation token) matches what the handler currently calls so the handler change is a one-line swap.
- The interface has an XML doc comment stating its purpose: "Leaflet-owned read-only abstraction over the knowledge base vector index."
- If the current `SearchSimilarAsync` return type is a KnowledgeBase domain type, a Leaflet-owned result DTO (class, per project rule) is introduced and the adapter maps to it. If the existing return type is already neutral (e.g. primitives or shared RAG types), no new DTO is needed.

### FR-2: KnowledgeBase implements the Leaflet contract via an adapter
Provide an adapter in the KnowledgeBase module that implements `ILeafletKnowledgeSource` and delegates to the existing `IKnowledgeBaseRepository`. The adapter lives in the KnowledgeBase module so KnowledgeBase owns the dependency direction (KnowledgeBase → Leaflet contract), not the other way around.

**Acceptance criteria:**
- A concrete class (e.g. `KnowledgeBaseLeafletSourceAdapter`) is added under `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/` or its infrastructure folder, following existing KnowledgeBase placement conventions.
- The adapter constructor takes `IKnowledgeBaseRepository` (or whatever the KnowledgeBase composition root uses) and forwards `SearchSimilarAsync` calls 1:1.
- DI registration wires `ILeafletKnowledgeSource` → the adapter in the KnowledgeBase module's `ServiceCollectionExtensions` (or equivalent), not in Leaflet's.
- No data shaping, filtering, or business logic is added in the adapter beyond DTO mapping when FR-1 requires it.

### FR-3: Update `GenerateLeafletHandler` to depend on the new abstraction
Replace the `using Anela.Heblo.Domain.Features.KnowledgeBase;` import and the `IKnowledgeBaseRepository` constructor parameter with `ILeafletKnowledgeSource`. All call sites inside the handler must use the new abstraction.

**Acceptance criteria:**
- `GenerateLeafletHandler.cs` contains no `using Anela.Heblo.Domain.Features.KnowledgeBase;` import.
- Constructor parameter type and field type are `ILeafletKnowledgeSource`.
- All existing handler behavior (return values, error paths, logging) is preserved — verified by existing unit tests passing without behavior-related modification.
- Existing unit tests for `GenerateLeafletHandler` are updated to mock `ILeafletKnowledgeSource` instead of `IKnowledgeBaseRepository`.

### FR-4: Relocate `DocumentType` to shared RAG infrastructure
Move the `DocumentType` enum out of `Anela.Heblo.Domain.Features.KnowledgeBase` and into `Anela.Heblo.Application.Shared.Rag`, alongside the existing `OneDriveFolderMapping` and `RagFeatureOptions`. The Leaflet ingestion job's filter (`LeafletIngestionJob.cs:62`) then references the shared location instead of the KnowledgeBase domain.

**Acceptance criteria:**
- The enum file moves from `backend/src/Anela.Heblo.Domain/Features/KnowledgeBase/` to `backend/src/Anela.Heblo.Application/Shared/Rag/DocumentType.cs`.
- Namespace becomes `Anela.Heblo.Application.Shared.Rag`.
- Enum members and their underlying integer values are **unchanged** (`KnowledgeBase`, `Conversation`, `Leaflet`, `Article` with identical numeric mapping) so the existing database column remains compatible — no EF Core migration is required.
- All current consumers are updated to import `using Anela.Heblo.Application.Shared.Rag;` instead of `using Anela.Heblo.Domain.Features.KnowledgeBase;`. Known consumers to update (verify exhaustively during implementation via reference search):
  - `KnowledgeBaseDocument`
  - `KnowledgeBaseChunk`
  - `OneDriveFolderMapping`
  - The EF Core configuration / value-conversion code and any migrations metadata referencing the enum
  - `LeafletIngestionJob`
- `LeafletIngestionJob.cs` contains no `using Anela.Heblo.Domain.Features.KnowledgeBase;` import after the change.
- The filter at line 62 continues to select the same set of mappings it does today (verified by a unit test or job-level test that exercises the mapping filter against a representative `LeafletOptions` configuration).
- `dotnet build` succeeds across the solution after the move; `dotnet format` passes.

### FR-5: Static enforcement that Leaflet does not reference KnowledgeBase domain
Add a hand-rolled xUnit reflection test so this regression cannot reappear silently.

**Acceptance criteria:**
- A new file `Architecture/ModuleBoundariesTests.cs` is added in the existing `backend/test/Anela.Heblo.Tests/` project, following the pattern established by `ReflectionValidationTests.cs`.
- The test loads the `Anela.Heblo.Application` assembly (`Assembly.Load("Anela.Heblo.Application")`), enumerates all types whose namespace starts with `Anela.Heblo.Application.Features.Leaflet`, and asserts that none of their referenced types live in the `Anela.Heblo.Domain.Features.KnowledgeBase` namespace.
- "Referenced types" includes: constructor parameters, fields, properties, method parameters, method return types, generic type arguments, and attribute types.
- An allowlist (initially empty) is included as a constant in the test file so future intentional exceptions can be made explicitly with a code comment justifying each entry.
- The test failure message identifies the offending type and the specific member (field/property/parameter) that introduced the reference.
- The test runs as part of the standard `dotnet test` invocation and fails CI if a future change reintroduces a direct reference.
- NetArchTest is **not** added as a dependency; no Roslyn analyzer or csproj reference-restriction is introduced.

### FR-6: Documentation update
Reflect the new module boundary in the architecture docs so the pattern is discoverable.

**Acceptance criteria:**
- `docs/architecture/development_guidelines.md` (or the closest existing section on cross-module communication) gains a short example showing the `ILeafletKnowledgeSource` pattern: contract owned by consumer, adapter owned by provider, DI registered by provider.
- The same section (or `docs/architecture/filesystem.md`, whichever is more appropriate) documents `Application/Shared/Rag/` as the canonical location for cross-module RAG infrastructure types, with `DocumentType` cited as an example.
- The Leaflet feature doc (if one exists under `docs/features/`) notes that knowledge-base search is consumed via `ILeafletKnowledgeSource`.

## Non-Functional Requirements

### NFR-1: Performance
The refactor MUST NOT add measurable latency to `GenerateLeafletHandler` or `LeafletIngestionJob`. The adapter is a thin pass-through with at most a DTO mapping; no extra allocations, copies, or async hops beyond the existing call. Vector search response time SHOULD remain within ±5% of pre-refactor baseline.

### NFR-2: Security
No change to authentication, authorization, or data sensitivity. The adapter exposes the same data already accessible to Leaflet — it does not widen or narrow access control. No new external services are introduced.

### NFR-3: Backwards compatibility
Public HTTP API surface of Leaflet is unchanged. No DTOs, request shapes, or response shapes are modified. Database schema is unchanged — `DocumentType` keeps identical underlying integer values, so the existing EF Core column mapping continues to work without a migration. The refactor is internal-only.

### NFR-4: Testability
After the refactor, `GenerateLeafletHandler` MUST be unit-testable with a mock `ILeafletKnowledgeSource` that has no dependency on KnowledgeBase types. The Leaflet test project MUST NOT need a project reference to anything in `Anela.Heblo.Domain.Features.KnowledgeBase` to test Leaflet handlers or the ingestion job.

### NFR-5: Code quality
All new types follow the project's existing conventions: DTOs as classes (not records), naming per `coding-style.md`, files under 800 lines, functions under 50 lines. Adapter, interface, and any new DTO each live in their own file. `dotnet build` and `dotnet format` pass after the change.

## Data Model
No persistent data model changes. Type-system changes:

- **New:** `ILeafletKnowledgeSource` interface (Leaflet module).
- **New:** `KnowledgeBaseLeafletSourceAdapter` class (KnowledgeBase module).
- **New (conditional):** Leaflet-owned `KnowledgeSearchResult` DTO — only if the current `SearchSimilarAsync` return type is a KnowledgeBase domain type. Class, not record.
- **Relocated:** `DocumentType` enum — moves from `Anela.Heblo.Domain.Features.KnowledgeBase` to `Anela.Heblo.Application.Shared.Rag`. Members and underlying integer values unchanged.
- **Unchanged:** `IKnowledgeBaseRepository` (KnowledgeBase keeps it; only the cross-module reference is removed via the new adapter).
- **Unchanged:** All database tables, EF Core entities, migrations. `LeafletOptions`, `RagFeatureOptions`, `OneDriveFolderMapping` retain their shape — only their `DocumentType` import path changes.

## API / Interface Design

### `ILeafletKnowledgeSource` (Leaflet-owned)
```csharp
namespace Anela.Heblo.Application.Features.Leaflet.Contracts;

/// <summary>
/// Leaflet-owned read-only abstraction over the knowledge base vector index.
/// Implemented by the KnowledgeBase module via an adapter.
/// </summary>
public interface ILeafletKnowledgeSource
{
    Task<IReadOnlyList<KnowledgeSearchResult>> SearchSimilarAsync(
        /* parameters matching current IKnowledgeBaseRepository.SearchSimilarAsync usage */,
        CancellationToken cancellationToken);
}
```
Exact parameter list and return type are taken verbatim from current usage in `GenerateLeafletHandler`. If the return type today is a KnowledgeBase domain type, define a Leaflet-owned `KnowledgeSearchResult` class and have the adapter map to it.

### `DocumentType` relocation
```csharp
namespace Anela.Heblo.Application.Shared.Rag;

public enum DocumentType
{
    // Same members and same underlying integer values as today.
    KnowledgeBase = /* existing value */,
    Conversation  = /* existing value */,
    Leaflet       = /* existing value */,
    Article       = /* existing value */,
}
```
Existing integer values must be preserved exactly.

### DI registration
KnowledgeBase composition root adds:
```csharp
services.AddScoped<ILeafletKnowledgeSource, KnowledgeBaseLeafletSourceAdapter>();
```
Leaflet composition root does **not** register this — provider owns the registration. Module-load order in DI must ensure KnowledgeBase registers its adapter before Leaflet resolves `ILeafletKnowledgeSource`; this is already handled by the existing `AddModule()`-style registration pattern but must be verified during implementation.

### No HTTP API changes
No new controllers, routes, MediatR requests, or OpenAPI changes. No frontend changes. No TypeScript client regeneration needed.

## Dependencies
- **Internal:** KnowledgeBase module (still depended on, but only via the new Leaflet-owned contract).
- **Internal:** Shared RAG infrastructure (`Application/Shared/Rag/`) — gains `DocumentType` as a new member of the namespace.
- **Internal:** Existing Leaflet unit tests; KnowledgeBase tests touching `IKnowledgeBaseRepository`; any test referencing `DocumentType` by its old namespace.
- **External libraries:** None added. The architecture test (FR-5) uses only existing xUnit and `System.Reflection`.
- **Tooling:** OpenAPI client regeneration not required (no public surface change). `dotnet build` and `dotnet format` must pass. `dotnet test` must pass, including the new boundary test.

## Out of Scope
- Refactoring `IKnowledgeBaseRepository` itself or any other KnowledgeBase internal structure beyond what FR-2 requires.
- Auditing other modules for similar cross-module violations (separate finding, separate spec).
- Performance optimization of vector search.
- Changes to the Leaflet ingestion pipeline behavior, scheduling, or OneDrive integration logic.
- Changes to public HTTP API, DTOs, or frontend code.
- Database schema changes or migrations.
- Restructuring `OneDriveFolderMapping`, `RagFeatureOptions`, or any other shared RAG type.
- Introducing NetArchTest, Roslyn analyzers, or csproj-level reference restrictions as alternatives to FR-5.

## Open Questions
None.

## Status: COMPLETE