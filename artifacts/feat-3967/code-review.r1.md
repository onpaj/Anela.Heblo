# Code Review: feat-3967 (round 1)

## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- None

## Notes

Reviewed the full branch diff against spec.r1.md. The functional code change is small
and mechanical, exactly matching the spec:

- `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IDqtEshopStockSource.cs`,
  `IDqtErpStockSource.cs`, `DqtEshopStockItem.cs`, `DqtErpStockItem.cs` — new
  DataQuality-owned contracts/DTOs (FR-1). Plain classes, no Catalog references, no
  speculative members. Matches spec verbatim.
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/DataQualityEshopStockSourceAdapter.cs`,
  `DataQualityErpStockSourceAdapter.cs` — new Catalog-side adapters (FR-2), `internal
  sealed`, pure delegation/projection. The `IsSellable` computation
  (`ProductTypeId == (int)ProductType.Goods || ProductTypeId == (int)ProductType.Product`)
  was moved verbatim from `ProductPairingDqtComparer`'s deleted private `IsSellable`
  helper — confirmed via `git diff` and by checking `ErpStock.ProductTypeId` is `int?`,
  so the lifted equality comparison preserves the original nullable-safe behavior
  (null → both comparisons false → `IsSellable = false`), matching the new adapter's own
  test `ListAsync_WhenProductTypeIdIsNull_IsSellableIsFalse`.
- `CatalogModule.cs` — two `AddScoped` registrations added in the exact location and
  comment style the spec specifies (FR-3), next to the sibling `IDqtResilienceService`
  registration. Verified `IEshopStockClient` is `AddHttpClient` (transient-by-default)
  and `IErpStockClient` is `AddSingleton` — a `Scoped` adapter depending on either is not
  a captive-dependency violation.
- `ProductPairingDqtComparer.cs` — rewired to the new contracts (FR-4): both Catalog
  `using` directives removed, constructor params/fields renamed and retyped, both
  resilience-wrapped call sites updated with operation names unchanged, the private
  `IsSellable` helper deleted, and the `.Where(IsSellable)` call updated to
  `.Where(p => p.IsSellable)`. Confirmed via grep that the file (and its test file) now
  contain zero references to `EshopStock`, `ErpStock`, `IEshopStockClient`,
  `IErpStockClient`, or `ProductType`. No other DataQuality file references these
  Catalog types either.
- `ModuleBoundariesTests.cs` — the four `DataQualityCatalogAllowlist` entries for
  `ProductPairingDqtComparer` are removed and the allowlist is now empty, with an
  updated comment matching the "Empty — ..." style of sibling allowlists (FR-5).
- `ProductPairingDqtComparerTests.cs` — mocks rebound from `IEshopStockClient`/
  `IErpStockClient` to `IDqtEshopStockSource`/`IDqtErpStockSource`, with
  `ProductTypeId = 1` replaced by `IsSellable = true` etc.; same scenarios, same
  assertions, only the mocked type shifts, as required.
- New adapter test files (`DataQualityEshopStockSourceAdapterTests.cs`,
  `DataQualityErpStockSourceAdapterTests.cs`) cover projection, the moved
  sellability logic (including the null-`ProductTypeId` edge case), and empty-input
  handling.

Verified independently in this review:
- `dotnet build Anela.Heblo.sln` → `Build succeeded. 0 Error(s)` (only pre-existing
  warnings, none new).
- The developer's own `artifacts/feat-3967/impl/full-verification.r2.md` already ran
  the full test suite and confirmed `ModuleBoundariesTests` (35 passed),
  `ProductPairingDqtComparerTests` (5 passed), `DataQualityEshopStockSourceAdapterTests`
  (3 passed), and `DataQualityErpStockSourceAdapterTests` (9 passed) all pass, with the
  only suite-wide failures being pre-existing Testcontainers/Docker environment
  failures unrelated to this change (confirmed via cross-commit comparison against the
  merge-base).
- `DataQualityCatalogAllowlist` is confirmed empty and the four allowlist entries the
  spec's background section cites (`ModuleBoundariesTests.cs` lines ~134–143) are gone.

This is a pure type-substitution/dependency-inversion refactor with no behavior change,
implemented exactly to spec, following the established `IStockOperationQuery` /
`DataQualityStockOperationQueryAdapter` precedent in the same file. No correctness
issues found; no cleanup findings worth raising.
