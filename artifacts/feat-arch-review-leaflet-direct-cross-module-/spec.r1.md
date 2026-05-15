# Specification: Decouple Leaflet Module from KnowledgeBase Domain

## Summary
The Leaflet module currently imports types directly from the KnowledgeBase domain layer, violating the project's "no direct cross-module references" rule. This spec defines a refactor that replaces these imports with a Leaflet-owned abstraction for vector search and a Leaflet-owned configuration type for document classification, restoring module independence.

## Background
`docs/architecture/development_guidelines.md` mandates that feature modules communicate only through contracts/interfaces, never through direct domain references. The daily architecture review on 2026-05-14 identified two violations in the Leaflet module:

1. **`GenerateLeafletHandler.cs`** depends on `IKnowledgeBaseRepository` from `Anela.Heblo.Domain.Features.KnowledgeBase` to perform `SearchSimilarAsync` vector lookups.
2. **`LeafletIngestionJob.cs`** depends on the `DocumentType` enum from the same KnowledgeBase domain namespace to filter `OneDriveFolderMappings`.

These couplings mean any rename or restructure inside the KnowledgeBase domain breaks Leaflet, the modules cannot be tested independently, and either module is harder to extract or replace in the future. The fix is to introduce a narrow Leaflet-owned contract for the search behavior and to relocate `DocumentType` (or replace its usage) so the Leaflet module no longer references KnowledgeBase types directly.

## Functional Requirements

### FR-1: Introduce Leaflet-owned vector search contract
Define a new interface `ILeafletKnowledgeSource` in the Leaflet feature's contract location (`backend/src/Anela.Heblo.Application/Features/Leaflet/Contracts/` or equivalent for that module's existing pattern). The interface must expose **only** the operations Leaflet actually consumes — currently `SearchSimilarAsync` with the exact parameter/return signature already used by `GenerateLeafletHandler`.

**Acceptance criteria:**
- `ILeafletKnowledgeSource` is declared inside the Leaflet module's namespace, not in KnowledgeBase.
- The interface surface is the minimum required by `GenerateLeafletHandler` — no additional methods carried over for speculative future use.
- The method signature (parameters, return type, async behavior, cancellation token) matches what the handler currently calls so the handler change is a one-line swap.
- The interface is documented with XML doc comments stating its purpose: "Leaflet-owned read-only abstraction over the knowledge base vector index."

### FR-2: KnowledgeBase implements the Leaflet contract via an adapter
Provide an adapter in the KnowledgeBase module that implements `ILeafletKnowledgeSource` and delegates to the existing `IKnowledgeBaseRepository`. The adapter lives in the KnowledgeBase module so KnowledgeBase owns the dependency direction (KnowledgeBase → Leaflet contract), not the other way around.

**Acceptance criteria:**
- A concrete class (e.g. `KnowledgeBaseLeafletSourceAdapter`) is added under `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/` or its infrastructure folder, following existing KnowledgeBase placement conventions.
- The adapter constructor takes `IKnowledgeBaseRepository` (or whatever the KnowledgeBase composition root uses) and forwards `SearchSimilarAsync` calls 1:1.
- DI registration wires `ILeafletKnowledgeSource` → the adapter in the KnowledgeBase module's `ServiceCollectionExtensions` (or equivalent), not in Leaflet's.
- No data shaping, filtering, or business logic is added in the adapter — it is a pure pass-through.

### FR-3: Update `GenerateLeafletHandler` to depend on the new abstraction
Replace the `using Anela.Heblo.Domain.Features.KnowledgeBase;` import and the `IKnowledgeBaseRepository` constructor parameter with `ILeafletKnowledgeSource`. All call sites inside the handler must use the new abstraction.

**Acceptance criteria:**
- `GenerateLeafletHandler.cs` contains no `using Anela.Heblo.Domain.Features.KnowledgeBase;` import.
- Constructor parameter type and field type are `ILeafletKnowledgeSource`.
- All existing handler behavior (return values, error paths, logging) is preserved — verified by existing unit tests passing without behavior-related modification.
- Existing unit tests for `GenerateLeafletHandler` are updated to mock `ILeafletKnowledgeSource` instead of `IKnowledgeBaseRepository`.

### FR-4: Replace `DocumentType` usage in `LeafletIngestionJob`
`DocumentType` is used at `LeafletIngestionJob.cs:62` to filter `OneDriveFolderMappings`. Move ownership of this concept out of the KnowledgeBase domain. The chosen approach is **Option A** (default, see Open Questions): introduce a Leaflet-owned enum or string constant inside Leaflet's options/configuration, decoupled from KnowledgeBase entirely.

**Acceptance criteria:**
- `LeafletIngestionJob.cs` contains no `using Anela.Heblo.Domain.Features.KnowledgeBase;` import.
- A Leaflet-owned type (enum or constants class, e.g. `LeafletDocumentType` under `Features/Leaflet/Configuration/` or `Features/Leaflet/Contracts/`) replaces the KnowledgeBase `DocumentType` references in the job.
- The filter on `OneDriveFolderMappings` continues to select the same set of mappings it does today (verified by job-level tests or an integration test that exercises the mapping filter).
- If `OneDriveFolderMappings` itself is a KnowledgeBase-owned type, the spec acknowledges this and the job consumes it only via a Leaflet-owned configuration projection or a dedicated mapping query in `ILeafletKnowledgeSource` — see Open Questions.

### FR-5: Static enforcement that Leaflet does not reference KnowledgeBase domain
Add a guard so this regression cannot reappear silently.

**Acceptance criteria:**
- Either an architecture test (e.g. using NetArchTest or a custom xUnit test scanning assembly references) asserts that no type in the `Anela.Heblo.Application.Features.Leaflet` namespace references `Anela.Heblo.Domain.Features.KnowledgeBase`, OR a build-time check (csproj reference rule, analyzer) enforces the same constraint.
- The test/check fails CI if a future change reintroduces a direct reference.

### FR-6: Documentation update
Reflect the new module boundary in the architecture docs so the pattern is discoverable.

**Acceptance criteria:**
- `docs/architecture/development_guidelines.md` (or the closest existing section on cross-module communication) gains a short example showing the `ILeafletKnowledgeSource` pattern: contract owned by consumer, adapter owned by provider.
- The Leaflet feature doc (if one exists under `docs/features/`) notes that knowledge-base search is consumed via `ILeafletKnowledgeSource`.

## Non-Functional Requirements

### NFR-1: Performance
The refactor MUST NOT add measurable latency to `GenerateLeafletHandler` or `LeafletIngestionJob`. The adapter is a thin pass-through; no extra allocations, copies, or async hops beyond the existing call. Vector search response time SHOULD remain within ±5% of pre-refactor baseline.

### NFR-2: Security
No change to authentication, authorization, or data sensitivity. The adapter exposes the same data already accessible to Leaflet — it does not widen or narrow access control. No new external services are introduced.

### NFR-3: Backwards compatibility
Public HTTP API surface of Leaflet is unchanged. No DTOs, request shapes, or response shapes are modified. Database schema is unchanged. The refactor is internal-only.

### NFR-4: Testability
After the refactor, `GenerateLeafletHandler` MUST be unit-testable with a mock `ILeafletKnowledgeSource` that has no dependency on KnowledgeBase types. The Leaflet test project MUST NOT need a project reference to anything in `Anela.Heblo.Domain.Features.KnowledgeBase`.

### NFR-5: Code quality
All new types follow the project's existing conventions: DTOs as classes (not records), naming per `coding-style.md`, files under 800 lines, functions under 50 lines. Adapter, interface, and configuration types each live in their own file.

## Data Model
No persistent data model changes. The only type-system changes are:

- **New:** `ILeafletKnowledgeSource` interface (Leaflet module).
- **New:** `KnowledgeBaseLeafletSourceAdapter` class (KnowledgeBase module).
- **New:** Leaflet-owned document-type representation (enum or constants) for ingestion job filtering — exact shape decided per Open Question Q1.
- **Unchanged:** `IKnowledgeBaseRepository`, `DocumentType` (KnowledgeBase keeps them; only the cross-module reference is removed).
- **Unchanged:** All database tables, EF Core entities, migrations.

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
Exact parameter list and return type are taken verbatim from current usage in `GenerateLeafletHandler`. If the current return type is a KnowledgeBase domain type, define a Leaflet-owned `KnowledgeSearchResult` DTO (class, per project rule) and have the adapter map to it.

### Leaflet-owned document type (FR-4)
Most likely a simple enum:
```csharp
namespace Anela.Heblo.Application.Features.Leaflet.Configuration;

public enum LeafletDocumentType { /* same members as today's filter requires */ }
```
Exact members come from inspecting the current filter at `LeafletIngestionJob.cs:62`.

### DI registration
KnowledgeBase composition root adds:
```csharp
services.AddScoped<ILeafletKnowledgeSource, KnowledgeBaseLeafletSourceAdapter>();
```
Leaflet composition root does **not** register this — provider owns the registration.

### No HTTP API changes
No new controllers, routes, MediatR requests, or OpenAPI changes. No frontend changes. No TypeScript client regeneration needed.

## Dependencies
- **Internal:** KnowledgeBase module (still depended on, but only via the new Leaflet-owned contract). Module-load order in DI must ensure KnowledgeBase registers its adapter before Leaflet resolves `ILeafletKnowledgeSource` — already handled by typical `AddModule()`-style registration patterns; verify during implementation.
- **Internal:** Existing Leaflet unit tests and any KnowledgeBase tests touching `IKnowledgeBaseRepository`.
- **External libraries:** None added. If architecture-test approach (FR-5) uses NetArchTest, add the NuGet package to the test project.
- **Tooling:** OpenAPI client regeneration not required (no public surface change). `dotnet build` and `dotnet format` must pass.

## Out of Scope
- Refactoring `IKnowledgeBaseRepository` itself or any other KnowledgeBase internal structure.
- Removing or refactoring `DocumentType` inside the KnowledgeBase module — it stays there for KnowledgeBase's own use.
- Auditing other modules for similar cross-module violations (separate finding, separate spec).
- Performance optimization of vector search.
- Changes to the Leaflet ingestion pipeline behavior, scheduling, or OneDrive integration logic.
- Changes to public HTTP API, DTOs, or frontend code.
- Database schema changes or migrations.

## Open Questions

### Q1: How should `DocumentType` be replaced in the ingestion job?
The brief suggests two options. The default assumption is **Option A** (Leaflet-owned enum), but the architect should confirm:
- **Option A (assumed):** Introduce `LeafletDocumentType` as a Leaflet-owned enum. Simplest, fully decoupled, but duplicates enum members if KnowledgeBase has the same concept.
- **Option B:** Move `DocumentType` to a shared `Application/Shared/` location used by multiple modules. Better if more than one consumer module needs it.
- **Option C:** Expose document-type filtering through `ILeafletKnowledgeSource` (e.g. a `GetMappingsAsync(filter)` method) so Leaflet never names the type at all. Best if `OneDriveFolderMappings` itself is also a KnowledgeBase type that Leaflet shouldn't reference.

Resolution needed before FR-4 implementation. The choice depends on whether `OneDriveFolderMappings` is owned by Leaflet or KnowledgeBase — implementer should inspect the type's namespace and decide accordingly, escalating if ambiguous.

### Q2: Is `OneDriveFolderMappings` a KnowledgeBase-owned type?
If yes, FR-4 is not sufficient on its own — Leaflet would still be reading a KnowledgeBase type even after removing the `DocumentType` import. The job may need to consume mappings via `ILeafletKnowledgeSource` (Option C above) rather than reading the KnowledgeBase collection directly.

### Q3: Architecture-test framework choice
NetArchTest vs. a hand-rolled reflection test vs. csproj/Analyzer-level enforcement (FR-5). Default to NetArchTest if the repo already uses it; otherwise a hand-rolled xUnit test reading assembly metadata is acceptable. Confirm before implementing FR-5.

## Status: HAS_QUESTIONS