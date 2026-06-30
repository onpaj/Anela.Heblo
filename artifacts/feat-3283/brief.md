## Module / File
backend/src/Anela.Heblo.Application/Features/Analytics/Services/ProductFilterService.cs

## Coverage
Line coverage: 0% (filter threshold: 60%)

## What's not tested
`ProductFilterService` is a pure service class with two methods, both at 0% coverage:

**`PassesFilters`** has two independent filter gates:
- Product name filter: case-insensitive `Contains` on `product.ProductName` — only applied when `productFilter` is non-null and non-whitespace
- Category filter: case-insensitive `Equals` on `product.ProductCategory` — only applied when `categoryFilter` is non-null and non-whitespace
- When both filters are null/empty, the method returns true unconditionally

**`FilterProductsAsync`** wraps the above in an async stream and applies a `maxProducts` cap:
- Stops consuming the async stream once `products.Count >= maxProducts` via `break`
- Products that don't pass filters are never added to the list

## Why it matters
Analytics filtering logic that silently returns wrong products (wrong category match, case sensitivity issue, early break at wrong count) would corrupt analytics views without any indication. Since both methods are stateless and pure, every branch is straightforward to test without infrastructure.

## Suggested approach
Unit tests (no mocking needed — inject test data via `IAsyncEnumerable`):
- productFilter = "Cream" → only products with "cream" (case-insensitive) in name pass
- categoryFilter = "Skincare" → only products in that category pass
- Both filters set → both conditions must hold simultaneously
- Both filters null → all products pass
- maxProducts = 3, stream has 10 matching → only 3 returned
- Empty stream → empty result
Estimated effort: ~1 hour.

---
_Filed by weekly coverage-gap routine on 2026-06-22. Based on CI run #27941952679 (9463aa5983b2a6d201782725aeeaaba777d8c07d)._
