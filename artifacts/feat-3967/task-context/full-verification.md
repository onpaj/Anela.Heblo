### task: full-verification

**Files:** none (verification only)

- [ ] **Step 1: Full solution build**

Run: `dotnet build Anela.Heblo.sln`
Expected: `Build succeeded.` with 0 errors — confirms `CatalogModule`, `ProductPairingDqtComparer`, and both new adapters compile together and no other file still references the old `ProductPairingDqtComparer` constructor signature.

- [ ] **Step 2: Run `dotnet format` verification**

Run: `dotnet format Anela.Heblo.sln --verify-no-changes`
Expected: exits 0 — no formatting violations in any of the new/modified files.

- [ ] **Step 3: Full backend test suite**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: `Passed!` with 0 failed — this includes `ModuleBoundariesTests`, `ProductPairingDqtComparerTests`, `DataQualityEshopStockSourceAdapterTests`, `DataQualityErpStockSourceAdapterTests`, and every other pre-existing test (confirms no other test file references the old `IEshopStockClient`/`IErpStockClient`-based constructor of `ProductPairingDqtComparer`).

- [ ] **Step 4: Grep sanity check for leftover references**

Run: `grep -rn "IEshopStockClient\|IErpStockClient\|Domain.Features.Catalog" backend/src/Anela.Heblo.Application/Features/DataQuality/Services/ProductPairingDqtComparer.cs backend/test/Anela.Heblo.Tests/Features/DataQuality/ProductPairingDqtComparerTests.cs`
Expected: no output (empty) — confirms FR-4/FR-5 acceptance criteria that neither file references `EshopStock`, `ErpStock`, `IEshopStockClient`, `IErpStockClient`, `ProductType`, or any `Anela.Heblo.Domain.Features.Catalog*`/`Anela.Heblo.Application.Features.Catalog*` namespace.

No commit for this task — it is verification-only. If any step fails, return to the task whose file caused the failure, fix it, and re-run this task's steps from the top.

---

## Self-Review

**1. Spec coverage:**
- FR-1 (DataQuality-owned contracts and DTOs) → `dqt-eshop-stock-contract`, `dqt-erp-stock-contract`.
- FR-2 (Catalog-side adapters, `IsSellable` mapping moved into the ERP adapter) → `catalog-eshop-stock-source-adapter`, `catalog-erp-stock-source-adapter`.
- FR-3 (DI registration in `CatalogModule`, `Scoped` lifetime, same comment block) → `catalog-module-di-registration`.
- FR-4 (rewire `ProductPairingDqtComparer`, remove Catalog `using`s and the `IsSellable` helper) → `rewire-product-pairing-comparer` Step 3.
- FR-5 (update allowlist and existing unit tests) → `rewire-product-pairing-comparer` Step 1 (tests) and `retire-dataquality-catalog-allowlist` (allowlist).
- NFR-1/NFR-2 (no perf/security impact) — no code changes required; the adapters do a single in-memory `Select().ToList()`, verified structurally in the adapter task implementations.
- NFR-3 (module isolation, enforced by `ModuleBoundariesTests`) → `retire-dataquality-catalog-allowlist` Step 2 confirms the rule still passes with an empty allowlist.
- Data Model / API Design sections describe no persisted or public-API changes — no task needed beyond the in-memory types already covered above.
- Out of Scope items (behavior changes, other comparers, combined `IProductPairingQuery`, changes to `IEshopStockClient`/`IErpStockClient`/`EshopStock`/`ErpStock`/`ProductType` themselves, performance optimization) — confirmed untouched by every task above; no task modifies those files.

**2. Placeholder scan:** No "TBD"/"implement later"/"add appropriate error handling" phrases anywhere above; every step carries the exact code or exact shell command with expected output. No task says "similar to Task N" — each task's code is fully spelled out even though the comparer and its test are logically paired with the two adapter tasks.

**3. Type consistency:** `DqtEshopStockItem` (`Code`, `PairCode`, `Name`) and `DqtErpStockItem` (`ProductCode`, `ProductName`, `IsSellable`) are defined once in `dqt-eshop-stock-contract`/`dqt-erp-stock-contract` and used with identical property names in `catalog-eshop-stock-source-adapter`, `catalog-erp-stock-source-adapter`, `rewire-product-pairing-comparer` (production code and test file). `IDqtEshopStockSource.ListAsync`/`IDqtErpStockSource.ListAsync` both return `Task<IReadOnlyList<T>>` everywhere they appear (contracts, adapters, comparer locals, test mocks) — the source asymmetry between `IEshopStockClient.ListAsync` (`List<EshopStock>`) and `IErpStockClient.ListAsync` (`IReadOnlyList<ErpStock>`) is normalized away at the adapter boundary in both `catalog-eshop-stock-source-adapter` and `catalog-erp-stock-source-adapter`, per the arch-review's explicit amendment. Constructor parameter order (`eshopStockSource, erpStockSource, resilienceService, logger`) matches between the production constructor in `rewire-product-pairing-comparer` Step 3 and the test's `CreateSut()` in Step 1.
