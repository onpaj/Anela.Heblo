# Implementation: register-shipment-delivery-checker-in-shipmentlabels-module

## What was implemented
Wired `IShipmentDeliveryChecker` → `ShipmentLabelsShipmentDeliveryCheckerAdapter` into DI inside `ShipmentLabelsModule`, following the consumer-owns-contract / provider-owns-adapter registration convention already used for `IPackingCarrierCoolingSource`/`IPackingProductSource`. The DI-wiring test (`ShipmentLabelsModuleTests`) was already present as an untracked file from a prior (aborted) developer run and matched the task-context spec exactly, so it was reused as-is rather than rewritten.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/ShipmentLabelsModule.cs` — added `using` statements for `ShipmentLabels.Infrastructure` and `ShoptetOrders.Contracts`, and added `services.AddTransient<IShipmentDeliveryChecker, ShipmentLabelsShipmentDeliveryCheckerAdapter>();` with an explanatory comment on the cross-module ownership convention.
- `backend/test/Anela.Heblo.Tests/Application/ShipmentLabels/ShipmentLabelsModuleTests.cs` — new test verifying `AddShipmentLabelsModule` resolves `IShipmentDeliveryChecker` as `ShipmentLabelsShipmentDeliveryCheckerAdapter`.

## Tests
`ShipmentLabelsModuleTests.AddShipmentLabelsModule_RegistersIShipmentDeliveryChecker_AsShipmentLabelsShipmentDeliveryCheckerAdapter` — builds a `ServiceCollection`, registers a mock `IShipmentClient`, calls `AddShipmentLabelsModule`, and asserts `GetRequiredService<IShipmentDeliveryChecker>()` resolves to the adapter type.

## How to verify
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ShipmentLabelsModuleTests"
dotnet build Anela.Heblo.sln
```
Both pass: 1/1 test passed, build succeeded with 0 errors (13 pre-existing unrelated warnings, plus the known unrelated `AccessMatrixGen` MSB3073 post-build-step warning present on a clean checkout of this branch too).

## Notes
The original developer subagent for this task repeatedly stalled waiting on its own backgrounded `dotnet test`/`dotnet build` invocations (an environment/tooling issue, not a code issue) and left the untracked test file on disk without committing. The orchestrator took over directly: verified the untracked test file matched the task-context spec verbatim, applied the DI registration change from the task-context exactly as specified, killed the stray dotnet/MSBuild processes left behind, and re-ran both commands synchronously to completion before committing.

## PR Summary
Registers the new `IShipmentDeliveryChecker` contract with its `ShipmentLabelsShipmentDeliveryCheckerAdapter` implementation in `ShipmentLabelsModule`'s DI container, using `AddTransient` — matching how the module's other cross-module adapters are wired. This is the DI-wiring half of the interface-ownership-inversion fix for `CompleteDeliveredOrdersJob`.

### Changes
- `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/ShipmentLabelsModule.cs`
- `backend/test/Anela.Heblo.Tests/Application/ShipmentLabels/ShipmentLabelsModuleTests.cs`

## Status
DONE
