# Design: Remove dead MockCatalogRepository from Persistence assembly

## No UI

This change has no user-facing surface. `MockCatalogRepository` is never wired into DI, never reached by any controller, MediatR handler, or view. There is nothing to wireframe or diagram — this section is intentionally omitted per the plan's scope (pure dead-code deletion).

## Component design

There is no new or restructured component. The change is the **removal** of one component:

| Component | Location | Role today | Disposition |
|---|---|---|---|
| `MockCatalogRepository` | `backend/src/Anela.Heblo.Persistence/Repositories/MockCatalogRepository.cs` | Unregistered `ICatalogRepository` implementation, 436 lines, returns hardcoded fabricated Czech product/stock/cost data. Zero callers, zero DI registration (confirmed by repo-wide grep). | **Delete entirely.** |
| `ICatalogRepository` | `Anela.Heblo.Domain.Features.Catalog` (interface, unaffected file) | Contract implemented by the mock and by the real repository. | **Untouched.** Still has exactly one implementation after this change. |
| `CatalogRepository` | Registered at `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs:49` | The sole real, DI-registered `ICatalogRepository` implementation. | **Untouched.** Remains the only implementation in the assembly graph. |

No new abstractions, no relocation, no test double is introduced — the plan explicitly rules that out (no current test consumer exists to justify one; adding one now would be speculative code against project guidance). If a genuine need for an in-memory `ICatalogRepository` test double arises later, it would be a new component under the test project (`Anela.Heblo.Tests` or equivalent), sized and reviewed against that concrete need at that time — not part of this change.

**Boundary after the change:** `Anela.Heblo.Persistence` contains exactly one `ICatalogRepository` implementation (the real one), matching the architecture rule in `docs/architecture/filesystem.md` that production persistence code lives in that assembly and test doubles do not.

## Data schemas

None apply. No DB schema, request/response contract, or event payload is touched:

- `ICatalogRepository`'s method signatures are unchanged.
- `CatalogAggregate`, `StockData`, `CatalogProperties`, and other domain types referenced by the deleted mock are defined elsewhere in the Domain project and are untouched — they lose one (unused) consumer, nothing else.
- No migration, no serialization format, no API contract is affected — the mock was never on any live code path, so there is no "before/after" data shape to reconcile.

## Implementation shape

Single-file deletion:

1. `git rm backend/src/Anela.Heblo.Persistence/Repositories/MockCatalogRepository.cs`
2. Verify via `grep -rln 'MockCatalogRepository' . --include='*.cs'` → no results.
3. `dotnet build` → clean (no consumers means no compile fallout).
4. `dotnet format` per project standard.
5. Run backend test suite to rule out any indirect dependency (e.g., assembly/reflection scanning) — none expected given `ICatalogRepository` is resolved purely through explicit DI registration in `CatalogModule.cs`.

No design decisions remain open; the plan already ruled out the only alternative (relocating a test double) as unjustified speculative work.
