# Design: Unit Test Coverage for GetMaterialsForPurchaseHandler

## Component Design

**`GetMaterialsForPurchaseHandlerTests`** (new test class)
`backend/test/Anela.Heblo.Tests/Features/Catalog/GetMaterialsForPurchaseHandlerTests.cs`, namespace `Anela.Heblo.Tests.Features.Catalog`.

Exercises the existing `GetMaterialsForPurchaseHandler` in isolation, with `ICatalogRepository` mocked (Moq). No production code changes.

Responsibilities:
- Construct a `Mock<ICatalogRepository>` and a `GetMaterialsForPurchaseHandler` instance per test (constructor injection), mirroring `GetProductUsageHandlerTests`.
- Provide a private `CreateCatalogItem(...)` fixture builder (object-initializer construction of `CatalogAggregate`, no domain lifecycle methods), parameterized by `productCode, productName, type, availableStock, location, purchaseHistory, minimalOrderQuantity` with sensible defaults.
- Set up `_catalogRepositoryMock.Setup(x => x.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(fixtures)` per test — the predicate argument is not evaluated; fixture lists are pre-filtered to represent what the repository would return for `Type == Material || Type == Goods`.
- Invoke `_handler.Handle(request, CancellationToken.None)` and assert on the returned `GetMaterialsForPurchaseResponse.Materials`.

Test groups (Facts/Theories), one per risk area from the spec:
- **Search-term filter (FR-2)**: match by `ProductCode` only, `ProductName` only, both, case-insensitivity, no-match exclusion, and null/empty/whitespace `SearchTerm` (no filtering applied) — collapsible into a `[Theory]` for the match-variant cases, with the empty-`SearchTerm` case as a separate `[Fact]` (different assertion shape).
- **Purchase-history price fallback (FR-3)**: empty `PurchaseHistory` → `LastPurchasePrice == null`; multi-record history → `LastPurchasePrice` equals the *last* record's `PricePerPiece`, not the first.
- **Filter-then-limit ordering (FR-4)**: `Limit` exceeding match count returns all matches; `Limit` below match count returns exactly `Limit` items that are all valid matches (assert via count/set-membership, not position, since `Take` runs before the final `OrderBy(ProductName)`); no-search-term case truncated by `Limit`; a dedicated test asserting the response is ordered alphabetically by `ProductName`.
- **Field mapping (FR-5)**: `ProductType` equals `item.Type.ToString()`; empty `Location`/`MinimalOrderQuantity` map to `null`, non-empty values pass through unchanged; `CurrentStock` asserted incidentally via non-zero `Stock.Available` in the FR-3/FR-4 fixtures.

No other components are added or modified.

## Data Schemas

No schema changes — tests consume existing types unmodified:

- `ICatalogRepository.FindAsync(Expression<Func<CatalogAggregate, bool>> predicate, CancellationToken ct) : Task<IEnumerable<CatalogAggregate>>` — mocked.
- `GetMaterialsForPurchaseRequest { string? SearchTerm, int Limit }` — constructed per test case.
- `GetMaterialsForPurchaseResponse { List<MaterialForPurchaseDto> Materials }` — assertion target.
- `MaterialForPurchaseDto { ProductCode, ProductName, ProductType, LastPurchasePrice, Location, CurrentStock, MinimalOrderQuantity }`.
- `CatalogAggregate` fixture shape (object initializer, only handler-relevant fields set): `ProductCode`, `ProductName`, `Type` (`ProductType` enum: `Material` / `Goods`), `Stock` (`StockData` with `Erp` set), `Location` (default `""`), `PurchaseHistory` (`IReadOnlyList<CatalogPurchaseRecord>`, plain settable), `MinimalOrderQuantity` (default `""`).
- `CatalogPurchaseRecord { PricePerPiece, ... }` — only `PricePerPiece` relevant.
