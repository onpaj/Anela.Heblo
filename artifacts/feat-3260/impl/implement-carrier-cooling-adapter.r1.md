# Implementation: implement-carrier-cooling-adapter

## What was implemented
Created CarrierCoolingPackingCarrierCoolingAdapter (internal sealed) implementing IPackingCarrierCoolingSource. Maps Carriers and DeliveryHandling enum names to strings. Registered in CarrierCoolingModule.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/CarrierCooling/Infrastructure/CarrierCoolingPackingCarrierCoolingAdapter.cs` — new adapter
- `backend/src/Anela.Heblo.Application/Features/CarrierCooling/CarrierCoolingModule.cs` — added registration
- `backend/test/Anela.Heblo.Tests/Features/CarrierCooling/Infrastructure/CarrierCoolingPackingCarrierCoolingAdapterTests.cs` — 2 unit tests

## Tests
2 tests covering enum-to-string mapping and empty list. All pass.

## How to verify
`dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "CarrierCoolingPackingCarrierCoolingAdapterTests"`

## Status
DONE
