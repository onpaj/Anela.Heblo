# Implementation: define-contracts

## What was implemented

Two consumer-owned contract interfaces (with their associated DTOs) were added to the `ShoptetOrders` Application module. These replace future direct repository injections in `ShoptetApiPackingOrderClient` with narrow, purpose-built abstractions.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Contracts/IPackingProductSource.cs` — `IPackingProductSource` interface returning a dictionary of `PackingProductInfo` keyed by product code; `PackingProductInfo` DTO with `Cooling`, `WeightGrams`, and `ImageUrl`.
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Contracts/IPackingCarrierCoolingSource.cs` — `IPackingCarrierCoolingSource` interface returning all carrier cooling settings; `PackingCarrierCoolingSetting` DTO with `CarrierName`, `DeliveryHandlingName`, and `Cooling`.

## Tests

No tests required for this task (interface definitions only).

## How to verify

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Build should succeed with no errors. Both new types will appear in the `Anela.Heblo.Application.Features.ShoptetOrders.Contracts` namespace.

## Status

DONE
