## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `backend/test/Anela.Heblo.Tests/Features/Catalog/GetMaterialsForPurchaseHandlerTests.cs:204-226` and `:229-254` — `Handle_LimitExceedsMatchCount_ReturnsAllMatchingItems` and `Handle_LimitBelowMatchCount_ReturnsExactlyLimitMatchingItems` assert set membership (`BeEquivalentTo`/`OnlyContain`) rather than the exact expected two items in order. Given the handler's fixed pipeline (`Where` preserves fixture order, `Take` slices the first N, then `OrderBy(ProductName)` sorts), the fixture data in both tests is arranged so the expected two items are actually deterministic and could be asserted with `.Should().Equal(...)` in the precise expected order for a slightly stronger/more readable assertion. Not required — the current assertions already correctly prove the filter-then-limit ordering per the spec's own suggested phrasing (FR-4).

### Notes
Verified: `ICatalogRepository.FindAsync` signature (`Task<IEnumerable<CatalogAggregate>> FindAsync(Expression<Func<CatalogAggregate,bool>>, CancellationToken)` via `IReadOnlyRepository<TEntity,TKey>`) matches the mock setup exactly. `CatalogAggregate.PurchaseHistory`, `Location`, `MinimalOrderQuantity`, `Stock` (backed by `StockData.Available = Erp + Transport + Manufactured` when `PrimaryStockSource == Erp`, the default) all behave as the tests assume. `ProductType` enum contains `Material` and `Goods` as used. `CatalogPurchaseRecord` has all fields referenced in the multi-record purchase-history fixture. Traced each test's expected outcome by hand against the handler's actual pipeline (type-predicate mock passthrough → optional case-insensitive `Contains` OR filter on `ProductCode`/`ProductName` → `Take(Limit)` → DTO mapping → `OrderBy(ProductName)`); all assertions match. This is a test-only diff; no production code was touched, consistent with the spec's "Out of Scope" section.
