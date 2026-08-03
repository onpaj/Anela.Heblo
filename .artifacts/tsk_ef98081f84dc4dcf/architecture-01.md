# Architecture review: Remove dead MockCatalogRepository from Persistence assembly

## Verdict

**Approved as scoped.** The plan and design (plain deletion, no relocation, no new test double) are correct and match every invariant I could check against the live codebase. No changes requested.

## Checks performed against the codebase (not just the docs)

1. **Reference count.** `grep -rln 'MockCatalogRepository' . --include='*.cs'` → only the file's own definition. No controller, handler, module, or test references it.

2. **No implicit/reflective DI path could resurrect it.** `CatalogModule.cs` registers `ICatalogRepository` explicitly (`services.AddTransient<ICatalogRepository, CatalogRepository>()` at line 49) — no `services.Scan(...)` over the Catalog or Persistence namespace. The only `Scan()` call in the Application layer is in `ManufactureModule.cs`, scoped to `Anela.Heblo.Application.Features.Manufacture.ErrorFilters.Filters` for `IManufactureErrorFilter` — unrelated namespace, unrelated interface. Checked all `assembly.GetTypes()` reflection tests (`ApplicationStartupTests`, `ErrorHandlingTests`, `TileIdContractTests`, `GateConsistencyTests`, `AccessMatrixTests`, `BankImportJobDiscoveryTests`) — none enumerate `Anela.Heblo.Persistence` for `ICatalogRepository` implementations or would break/change count after deletion.

3. **A correct test double for `ICatalogRepository` already exists — separately, in the right place.** `backend/test/Anela.Heblo.Tests/Common/ManufactureOrderTestFactory.cs` defines `TestCatalogRepository : ICatalogRepository` and wires it via `services.Replace(ServiceDescriptor.Transient<ICatalogRepository, TestCatalogRepository>())`. This is the pattern `docs/architecture/filesystem.md` and `CatalogModule.cs`'s own comment ("Register default implementations - tests can override these") describe. It confirms two things: (a) the project already follows "test doubles live in the test project, not Persistence" correctly for this exact interface, and (b) no test is starved for a double — `MockCatalogRepository` was never that double, it was orphaned parallel dead code. This rules out the plan's "relocate" alternative on solid evidence, not just absence of a currently-known consumer.

4. **Precedent is a clean match, not just thematically similar.** Commit `3a1b69d4` (#3253) deleted `InMemoryPurchaseOrderRepository`/`InMemoryPurchaseOrderNumberGenerator` from the production Application project as a pure file deletion, dropping only the tests that exercised the stub's own reimplementation — no relocation, no shim. Commit `cb3feb48` (#3705) did the same for `MockSupplierRepository`'s dead method. Both are direct architectural precedent for "delete outright," reviewed and merged. This task is the same defect class, same fix shape.

5. **No hidden data/schema coupling.** The mock only touches `CatalogAggregate`/`StockData`/`CatalogProperties`, which are plain Domain types with no persistence mapping tied to the mock (EF configurations for the real `CatalogRepository` are separate, under `Anela.Heblo.Persistence.Catalog.*`, untouched). Deleting the mock changes zero schema, zero contract.

## Alignment with `docs/architecture/filesystem.md`

- `Anela.Heblo.Persistence` is documented as the Infrastructure layer holding "shared repository implementations" — production code only. Test doubles belong under the test project.
- After deletion, `Anela.Heblo.Persistence` holds exactly one `ICatalogRepository` implementation (`CatalogRepository`), and the test project already holds its own (`TestCatalogRepository`). This is the target end-state the doc describes, and it's reached by deleting, not by adding anything.

## Risks

None material for a single dead-file deletion with zero references and a verified absence of reflective/DI-scan pickup. The only theoretical risk — some out-of-repo tool or script depending on the file's existence — is outside the review's reach and not a codebase invariant; not a blocker.

## Prerequisites before implementation

None. The plan's verification steps (grep re-check, `dotnet build`, `dotnet format`, run backend tests) are sufficient and already correctly ordered in `plan-01.md`. Proceed directly to implementation.
