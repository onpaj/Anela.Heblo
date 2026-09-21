# Implementation: expeditionlist-adapter

## What was implemented
Implemented `ShoptetOrdersOrderStatusReaderAdapter`, an internal adapter in the ShoptetOrders
module that implements ExpeditionList's `IOrderStatusReader` contract by delegating to the
existing `IEshopOrderClient.GetOrderStatusIdAsync`. Registered the adapter in
`ShoptetOrdersModule.AddShoptetOrdersModule`, mirroring the existing `IPackedOrderStatusUpdater`
registration pattern (Transient lifetime, provider-owns-registration).

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Infrastructure/ShoptetOrdersOrderStatusReaderAdapter.cs` — new internal sealed adapter class implementing `IOrderStatusReader` by delegating to `IEshopOrderClient.GetOrderStatusIdAsync`
- `backend/test/Anela.Heblo.Tests/Features/ShoptetOrders/Infrastructure/ShoptetOrdersOrderStatusReaderAdapterTests.cs` — new test file with 2 tests: delegation with same arguments/result, and unmodified propagation of `HttpRequestException` with `StatusCode == NotFound`
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/ShoptetOrdersModule.cs` — added `using Anela.Heblo.Application.Features.ExpeditionList.Contracts;` and `services.AddTransient<IOrderStatusReader, ShoptetOrdersOrderStatusReaderAdapter>();` with the same cross-module-contract comment style used for the existing `IPackedOrderStatusUpdater` registration

## Tests
- `ShoptetOrdersOrderStatusReaderAdapterTests.GetOrderStatusIdAsync_DelegatesToEshopOrderClient_WithSameArgumentsAndResult` — verifies the adapter forwards order code and cancellation token unchanged and returns the client's result
- `ShoptetOrdersOrderStatusReaderAdapterTests.GetOrderStatusIdAsync_PropagatesNotFoundHttpRequestException_Unmodified` — verifies the 404 `HttpRequestException` shape required by `PrintExpeditionOrderHandler`'s existing 404 handling propagates unmodified

Both tests pass:
```
Passed!  - Failed:     0, Passed:     2, Skipped:     0, Total:     2, Duration: 14 ms - Anela.Heblo.Tests.dll (net8.0)
```

## How to verify
```
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ShoptetOrdersOrderStatusReaderAdapterTests"
```

## Notes
Followed the task-context file verbatim — code matches the specified adapter, test, and module
registration exactly. No deviations. `IOrderStatusReader` contract and its 404-propagation
contract comment already existed from the `expeditionlist-contract` task; this task only added
the ShoptetOrders-side implementation and DI wiring.

## PR Summary
Added `ShoptetOrdersOrderStatusReaderAdapter`, the ShoptetOrders-side implementation of
ExpeditionList's `IOrderStatusReader` contract, delegating to `IEshopOrderClient.GetOrderStatusIdAsync`
and registering it in `ShoptetOrdersModule`. This mirrors the pattern already used for
`IPackedOrderStatusUpdater`/Packaging, continuing the module-boundary cleanup for issue #4209.

### Changes
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Infrastructure/ShoptetOrdersOrderStatusReaderAdapter.cs` — new adapter
- `backend/test/Anela.Heblo.Tests/Features/ShoptetOrders/Infrastructure/ShoptetOrdersOrderStatusReaderAdapterTests.cs` — new tests
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/ShoptetOrdersModule.cs` — DI registration

## Status
DONE
