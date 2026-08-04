# Plan: Remove dead MockCatalogRepository from Persistence assembly

## Summary

`MockCatalogRepository` (436 lines, `backend/src/Anela.Heblo.Persistence/Repositories/MockCatalogRepository.cs`) is an unused `ICatalogRepository` implementation returning hardcoded, fabricated catalog data. It has no DI registration and no callers anywhere in the repo — confirmed via `grep -rln 'MockCatalogRepository' . --include='*.cs'`, which returns only the file's own definition. The real, registered implementation is `CatalogRepository`, wired at `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs:49`. This is a pure deletion task: remove the dead file, confirm nothing breaks.

## Context

Same defect class already fixed twice in this codebase (#3253, #3705): a mock repository implementation shipped inside a production assembly (`Anela.Heblo.Persistence`) instead of a test project. Per `docs/architecture/filesystem.md`, production persistence code belongs in `Anela.Heblo.Persistence`; test doubles belong in the test project. Beyond clutter, a stray/copy-pasted DI registration pointing at this class in future module wiring would silently serve fabricated stock/pricing/cost data to costing, margin, and stock-analysis consumers with no compile-time signal — a footgun, not just dead weight.

## Functional requirements

- **FR-1: Delete the dead mock repository.**
  Remove `backend/src/Anela.Heblo.Persistence/Repositories/MockCatalogRepository.cs` entirely.
  Acceptance: file no longer exists; `grep -rln 'MockCatalogRepository' . --include='*.cs'` returns no results.

- **FR-2: No behavior change.**
  No DI registration, controller, service, or test currently references `MockCatalogRepository` — removal must not require any other code changes.
  Acceptance: `dotnet build` succeeds with zero new errors/warnings after deletion; `git status` shows only the one file deleted (no edits elsewhere required).

- **FR-3: Preserve real catalog behavior.**
  `CatalogRepository` remains the sole registered `ICatalogRepository` implementation (`CatalogModule.cs:49` untouched).
  Acceptance: existing Catalog-related unit/integration tests pass unchanged.

## Non-functional requirements

- **Reversibility:** trivial — a single file deletion, fully recoverable from git history if a genuine catalog test double is ever needed.
- **No security/perf implications** — dead code removal only, no runtime path affected.

## Data model

None — no persisted entities or schema involved. `CatalogAggregate`/`StockData`/`CatalogProperties` etc. referenced by the mock are domain types defined elsewhere and remain untouched; only their fabricated-data consumer (the mock class) is removed.

## Interfaces

None — `ICatalogRepository` interface itself is untouched; only one of its (unused) implementations is deleted.

## Dependencies and scope

**In scope:**
- Delete `backend/src/Anela.Heblo.Persistence/Repositories/MockCatalogRepository.cs`.

**Explicitly out of scope:**
- Relocating a catalog test double to the test assembly. The issue's "suggested direction" mentions this as an option ("if a catalog test double is still wanted"), but there is no current consumer or test demand for one — confirmed no test project references it. Per "surgical changes" / no speculative work, do not create a new test double nobody asked for. If a future test needs an in-memory `ICatalogRepository`, it can be added at that time under the test project.
- No other files in `Anela.Heblo.Persistence` are touched.
- No changes to `CatalogModule.cs`, `CatalogRepository`, or `ICatalogRepository`.

## Rough plan

1. Delete `backend/src/Anela.Heblo.Persistence/Repositories/MockCatalogRepository.cs`.
2. Re-run repo-wide grep for `MockCatalogRepository` to confirm zero remaining references.
3. `dotnet build` the solution — confirm it compiles cleanly (the class had no consumers, so this should be a no-op build-wise).
4. `dotnet format` per project validation standard.
5. Run the backend test suite (or at minimum Catalog-related test projects) to confirm no hidden dependency on the mock (e.g. via reflection or assembly scanning) exists.
6. Commit as a standalone dead-code removal change.

## Open questions

- None. The issue evidence is unambiguous (zero external references, no DI registration), and the fix precedent (#3253, #3705) is a straight deletion, not a relocation. Defaulting to deletion-only per project's "don't add speculative code" guidance.
