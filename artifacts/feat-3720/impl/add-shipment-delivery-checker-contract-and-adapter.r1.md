# Implementation: add-shipment-delivery-checker-contract-and-adapter

## What was implemented
Added a new consumer-owned contract `IShipmentDeliveryChecker` (owned by `ShoptetOrders`) and a provider-owned adapter `ShipmentLabelsShipmentDeliveryCheckerAdapter` (owned by `ShipmentLabels`) that implements it by delegating to the existing `IShipmentClient.HasDeliveredShipmentAsync`. This mirrors the existing cross-module contract/adapter pattern used by `CarrierCooling` -> `IPackingCarrierCoolingSource`. A unit test verifies the adapter delegates arguments and return values (including cancellation token) unchanged to `IShipmentClient`.

Followed strict TDD: wrote the adapter test first, ran it and confirmed it failed to compile with `CS0234: The type or namespace name 'Infrastructure' does not exist in the namespace 'Anela.Heblo.Application.Features.ShipmentLabels'` (exactly as predicted by the task), then created the contract and adapter, reran the test to green, then built the whole solution.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Contracts/IShipmentDeliveryChecker.cs` — new consumer-owned contract interface with `Task<bool> HasDeliveredShipmentAsync(string orderCode, CancellationToken ct = default)`.
- `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/Infrastructure/ShipmentLabelsShipmentDeliveryCheckerAdapter.cs` — new `internal sealed` adapter class implementing `IShipmentDeliveryChecker` by delegating to `IShipmentClient` (new `Infrastructure/` folder under `ShipmentLabels`, mirroring `CarrierCooling/Infrastructure/`).
- `backend/test/Anela.Heblo.Tests/Features/ShipmentLabels/Infrastructure/ShipmentLabelsShipmentDeliveryCheckerAdapterTests.cs` — new unit test file (new folder), mirroring `CarrierCoolingPackingCarrierCoolingAdapterTests.cs`.

## Tests
- `ShipmentLabelsShipmentDeliveryCheckerAdapterTests.HasDeliveredShipmentAsync_DelegatesToShipmentClient_WithSameArgumentsAndResult` — verifies the adapter passes through order code and cancellation token unchanged and returns the mocked `true` result, and verifies the underlying `IShipmentClient.HasDeliveredShipmentAsync` was invoked exactly once with the same arguments.
- `ShipmentLabelsShipmentDeliveryCheckerAdapterTests.HasDeliveredShipmentAsync_ReturnsFalse_WhenShipmentClientReturnsFalse` — verifies a `false` result from `IShipmentClient` flows through unchanged, using the default cancellation token overload.

Both tests pass: `Passed! - Failed: 0, Passed: 2, Skipped: 0, Total: 2`.

## How to verify
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ShipmentLabelsShipmentDeliveryCheckerAdapterTests"
dotnet build ../Anela.Heblo.sln
```
Both commands were run during implementation: the filtered test run reports 2/2 passing, and the full solution build reports `Build succeeded. 0 Error(s)` (13 pre-existing warnings unrelated to this change, plus a pre-existing non-fatal `MSB3073` warning from the `AccessMatrixGen` post-build tool crashing on JSON parsing — unrelated to this change and present before it).

## Notes
- No dependency-injection registration was added for `IShipmentDeliveryChecker` / `ShipmentLabelsShipmentDeliveryCheckerAdapter` — the task instructions covered only the contract, adapter, and adapter unit test (steps 1-6), with no DI wiring step specified. This mirrors the task scope exactly; wiring (`services.AddTransient<IShipmentDeliveryChecker, ShipmentLabelsShipmentDeliveryCheckerAdapter>()` in a module class) is presumably a follow-up task once a consumer is introduced.
- Local environment note (not a code issue): the first two `dotnet test`/`dotnet build` invocations in this environment hung indefinitely after the pre-existing `AccessMatrixGen` tool crash, apparently due to a stale/orphaned MSBuild node process left over from an earlier run. Killing the stray `dotnet`/`MSBuild` processes and rerunning with `MSBUILDDISABLENODEREUSE=1 ... --nodeReuse:false` resolved it and both the test run and full solution build completed normally and quickly. This is an environment quirk unrelated to the code change.
- `artifacts/feat-3720/state.json` had a pre-existing uncommitted modification in the worktree before this task started; it was left untouched and not included in the commit, per instructions to stage only the specific task files.

## PR Summary
This change introduces the `IShipmentDeliveryChecker` contract, owned by the `ShoptetOrders` module, and its `ShipmentLabelsShipmentDeliveryCheckerAdapter` implementation, owned by the `ShipmentLabels` module. The adapter is a thin pass-through over the existing `IShipmentClient.HasDeliveredShipmentAsync(orderCode, ct)` method, following the established cross-module contract/adapter convention already used for `IPackingCarrierCoolingSource` (`CarrierCooling` -> `ShoptetOrders`). This lays the groundwork for `ShoptetOrders` to check shipment delivery status without depending directly on `ShipmentLabels` internals. The change was developed test-first: the adapter test was written and confirmed to fail to compile before the contract and adapter were implemented, then confirmed to pass afterward, and the full solution build was verified clean (0 errors).

### Changes
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Contracts/IShipmentDeliveryChecker.cs` (new)
- `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/Infrastructure/ShipmentLabelsShipmentDeliveryCheckerAdapter.cs` (new)
- `backend/test/Anela.Heblo.Tests/Features/ShipmentLabels/Infrastructure/ShipmentLabelsShipmentDeliveryCheckerAdapterTests.cs` (new)

## Status
DONE
