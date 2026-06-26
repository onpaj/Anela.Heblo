## Module
Leaflet

## Finding
`backend/src/Anela.Heblo.Domain/Features/Leaflet/ILeafletRepository.cs` defines a single interface with 15 methods spanning two distinct aggregate roots and a persistence infrastructure concern:

**Document/Chunk aggregate** (lines 5–22):
- `AddDocumentAsync`, `AddChunksAsync`, `GetByHashAsync`, `GetBySourcePathAsync`, `GetByGraphItemIdAsync`, `DeleteDocumentAsync`, `SearchSimilarAsync`, `UpdateSourcePathAsync`, `UpdateGraphItemIdAsync`, `UpdateStatusAsync`, `GetDocumentsPagedAsync`, `GetDistinctContentTypesAsync`, `GetChunkByIdAsync`, `GetFirstChunkIdsByDocumentIdsAsync`

**Generation/Feedback aggregate** (lines 24–29):
- `SaveGenerationAsync`, `GetGenerationByIdAsync`, `GetGenerationsPagedAsync`, `GetGenerationStatsAsync`

**Persistence infrastructure** (line 23):
- `SaveChangesAsync`

## Why it matters
This violates the Interface Segregation Principle: handlers that only need document operations (e.g. `GetLeafletDocumentsHandler`, `IndexLeafletHandler`) must depend on generation/feedback methods they never call, and vice versa. As the module grows, every new generation query adds to an interface that document-focused handlers must mock in tests. `SaveChangesAsync` exposes a unit-of-work concern through a domain repository interface, which is a Clean Architecture boundary leak.

## Suggested fix
Split into two narrower interfaces:
- `ILeafletDocumentRepository` — document/chunk read/write operations and `SearchSimilarAsync`
- `ILeafletGenerationRepository` — `SaveGenerationAsync`, `GetGenerationByIdAsync`, `GetGenerationsPagedAsync`, `GetGenerationStatsAsync`

`SaveChangesAsync` should be removed from the domain interface and called internally in the persistence implementation, or surfaced through a separate `ILeafletUnitOfWork` if transaction control is needed by handlers.

---
_Filed by daily arch-review routine on 2026-05-14._