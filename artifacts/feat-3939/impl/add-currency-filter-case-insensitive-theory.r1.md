# Implementation: add-currency-filter-case-insensitive-theory

## What was implemented
Added FR-4 coverage to `ShoptetApiInvoiceSourceTests`: a `[Theory]` test,
`GetAllAsync_ListModeCurrencyFilter_IsCaseInsensitive`, with two `[InlineData]`
cases (`"czk"`/`"CZK"` and `"CZK"`/`"czk"`) proving the in-memory currency
filter in `ShoptetApiInvoiceSource.GetAllAsync` matches regardless of casing
on either side of the comparison. This exercises the
`StringComparison.OrdinalIgnoreCase` comparison used in the production code's
`matchingCodes` filter.

## Files created/modified
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetApiInvoiceSourceTests.cs` — added the new `[Theory]` method after the existing FR-3 test; FR-1/FR-2/FR-3 tests are unchanged.

## Tests
- `GetAllAsync_ListModeCurrencyFilter_IsCaseInsensitive` (Theory, 2 cases) — asserts the single invoice in the batch is returned and `GetInvoiceAsync("A", ...)` is called exactly once, for both directions of casing mismatch between the invoice summary's currency and the query's requested currency.

## How to verify
```bash
dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj --filter "FullyQualifiedName~ShoptetApiInvoiceSourceTests"
```
Result: `Passed! - Failed: 0, Passed: 5, Skipped: 0` (FR-1, FR-2, FR-3, FR-4 x2 InlineData cases).

## Notes
No production code changes were needed — the filter in `ShoptetApiInvoiceSource.cs` already uses `StringComparison.OrdinalIgnoreCase`, so this task is pure test coverage confirming existing behavior.

## PR Summary
Adds a `[Theory]`-based test proving the Shoptet invoice list-mode currency filter is case-insensitive in both casing directions, closing part of the FR-4 coverage gap called out in issue #3939.

### Changes
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetApiInvoiceSourceTests.cs` — added `GetAllAsync_ListModeCurrencyFilter_IsCaseInsensitive` theory test

## Status
DONE
