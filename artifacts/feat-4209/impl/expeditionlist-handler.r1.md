# Implementation: expeditionlist-handler

## What was implemented

Swapped `PrintExpeditionOrderHandler`'s dependency from `ShoptetOrders.IEshopOrderClient`
(the full 11-method Shoptet client interface) to `ExpeditionList.Contracts.IOrderStatusReader`
(a single-method consumer-owned contract with `GetOrderStatusIdAsync`), following the
consumer-owns-contract pattern established for `IShipmentDeliveryChecker` /
`ILeafletKnowledgeSource` and already applied to `Packaging` in earlier tasks of this feature.
`IOrderStatusReader` and its `ShoptetOrders`-side adapter
(`ShoptetOrdersOrderStatusReaderAdapter`) were already created by the prior
`expeditionlist-contract` and `expeditionlist-adapter` tasks; this task only rewires the
consuming handler and its tests to the new interface.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/PrintExpeditionOrder/PrintExpeditionOrderHandler.cs`
  — removed the `ShoptetOrders` using, replaced the `IEshopOrderClient` field/constructor
  parameter/usage with `IOrderStatusReader`. The existing 404 handling
  (`catch (HttpRequestException ex) when (ex.StatusCode == NotFound)`) is untouched — it
  works unchanged against whatever the injected reader throws.
- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/PrintExpeditionOrderHandlerTests.cs`
  — removed the `ShoptetOrders` using, changed the mock field to
  `Mock<IOrderStatusReader>`. No other test code changed: `IOrderStatusReader.GetOrderStatusIdAsync`
  has an identical signature to the old `IEshopOrderClient` method, so every existing
  `_client.Setup(...)` call kept working unchanged.

## Tests

`PrintExpeditionOrderHandlerTests` (9 test cases, including the `[Theory]` with 4
`InlineData` rows) — covers non-printable states, the happy path, the "nothing printed"
error path, the 404-not-found path, the 500-propagates path, and the configured
(non-hardcoded) `DesiredStateId` path. All pass unchanged against the new mock type.

## How to verify

```bash
cd backend
dotnet build test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PrintExpeditionOrderHandlerTests"
dotnet build Anela.Heblo.sln
```

Results: test project build succeeded (0 errors); all 9 `PrintExpeditionOrderHandlerTests`
passed; full solution build succeeded (0 errors, pre-existing nullable warnings only,
unrelated to this change).

## Notes

No deviations from the task context — the 5 steps (update tests, update handler, run
filtered tests, full solution build, commit) were followed exactly as specified. No new
warnings were introduced by this change.

## PR Summary

Rewired `PrintExpeditionOrderHandler` (ExpeditionList module) off the cross-module
`ShoptetOrders.IEshopOrderClient` and onto the consumer-owned `IOrderStatusReader`
contract, completing the `ExpeditionList` half of this issue's consumer-owns-contract
fix (the `Packaging` half was completed in earlier tasks). This is the last handler-level
change; only `module-boundary-enforcement` remains before the feature is done.

### Changes
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/PrintExpeditionOrder/PrintExpeditionOrderHandler.cs` — depends on `IOrderStatusReader` instead of `IEshopOrderClient`
- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/PrintExpeditionOrderHandlerTests.cs` — mocks `IOrderStatusReader` instead of `IEshopOrderClient`

## Status
DONE
