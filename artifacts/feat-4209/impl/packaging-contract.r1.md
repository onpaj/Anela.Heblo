# Implementation: packaging-contract

## What was implemented
Added the consumer-owned contract interface `IPackedOrderStatusUpdater` in the
`Packaging` module, narrowing the surface `Packaging` handlers need from
`ShoptetOrders.IEshopOrderClient` down to the single `MarkAsPackedAsync` method
they actually use. This is the first of the tasks that decouple `Packaging`
from directly consuming `IEshopOrderClient`.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Packaging/Contracts/IPackedOrderStatusUpdater.cs` — new consumer-owned contract interface with a single `MarkAsPackedAsync(string orderCode, CancellationToken ct = default)` method, following the `IShipmentDeliveryChecker` / `ILeafletKnowledgeSource` pattern.

## Tests
No tests required for this task — it adds a currently-unused interface (no implementation or consumer wired up yet; that happens in `packaging-adapter` and `packaging-handlers`).

## How to verify
```
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```
Expected: Build succeeded, 0 errors (pre-existing warnings in unrelated files are unaffected).

## Notes
Followed the task-context file's Step 1 code exactly, matching the existing `IShipmentDeliveryChecker` namespace/doc-comment style in `ShoptetOrders.Contracts`. No deviations.

## PR Summary
Added `IPackedOrderStatusUpdater`, a consumer-owned contract interface in `Packaging.Contracts`, exposing only the single `MarkAsPackedAsync` method that `Packaging` handlers need — the first step in decoupling `Packaging` from directly injecting `ShoptetOrders.IEshopOrderClient`.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Packaging/Contracts/IPackedOrderStatusUpdater.cs` — new file, consumer-owned contract interface

## Status
DONE
