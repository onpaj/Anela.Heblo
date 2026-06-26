# Implementation: implement-catalog-adapter

## What was implemented
Created CatalogPackingProductSourceAdapter (internal sealed) that implements IPackingProductSource. Registered in CatalogModule. Weight fallback logic (GrossWeight → NetWeight → null) moved from ShoptetApiPackingOrderClient into the adapter.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogPackingProductSourceAdapter.cs` — new adapter
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` — added registration
- `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogPackingProductSourceAdapterTests.cs` — 5 unit tests

## Tests
5 tests covering cooling mapping, GrossWeight priority, NetWeight fallback, null weight, and ImageUrl mapping. All pass.

## How to verify
`dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "CatalogPackingProductSourceAdapterTests"`

## Status
DONE
