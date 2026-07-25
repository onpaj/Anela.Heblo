### task: swap-complete-delivered-orders-job-to-new-contract

Retype `CompleteDeliveredOrdersJob`'s dependency from `IShipmentClient` to `IShipmentDeliveryChecker`, and update its existing unit tests to mock the new interface. No change to `ExecuteAsync` control flow, logging, or job metadata.

**Step 1 — update the test file's mock type first (red step).**

File: `backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/CompleteDeliveredOrdersJobTests.cs`.

Change the `using` block at the top from:
```csharp
using Anela.Heblo.Application.Features.FeatureFlags;
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Features.ShoptetOrders.Infrastructure.Jobs;
```
to:
```csharp
using Anela.Heblo.Application.Features.FeatureFlags;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Features.ShoptetOrders.Contracts;
using Anela.Heblo.Application.Features.ShoptetOrders.Infrastructure.Jobs;
```

Change the `MakeSut` return-tuple type and body from:
```csharp
    private static (
        CompleteDeliveredOrdersJob Sut,
        Mock<IEshopOrderClient> Orders,
        Mock<IShipmentClient> Shipments,
        Mock<IRecurringJobStatusChecker> StatusChecker)
        MakeSut(bool jobEnabled = true, bool applyChanges = true, bool useTestSource = false)
    {
        var orders = new Mock<IEshopOrderClient>();
        var shipments = new Mock<IShipmentClient>();
```
to:
```csharp
    private static (
        CompleteDeliveredOrdersJob Sut,
        Mock<IEshopOrderClient> Orders,
        Mock<IShipmentDeliveryChecker> Shipments,
        Mock<IRecurringJobStatusChecker> StatusChecker)
        MakeSut(bool jobEnabled = true, bool applyChanges = true, bool useTestSource = false)
    {
        var orders = new Mock<IEshopOrderClient>();
        var shipments = new Mock<IShipmentDeliveryChecker>();
```

No other lines in this file change — every `shipments.Setup(...)` / `shipments.Verify(...)` call site references `HasDeliveredShipmentAsync`, whose signature is identical on `IShipmentDeliveryChecker`.

Confirm this alone breaks the build (the job's constructor still expects `IShipmentClient`, so passing `shipments.Object` of type `IShipmentDeliveryChecker` into `new CompleteDeliveredOrdersJob(...)` at line 44-46 fails to compile):

```bash
dotnet build Anela.Heblo.sln
```

Expect a compile error in `CompleteDeliveredOrdersJobTests.cs` about the constructor argument type.

**Step 2 — update the job itself (green step).**

File: `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Infrastructure/Jobs/CompleteDeliveredOrdersJob.cs`.

Change line 2 from:
```csharp
using Anela.Heblo.Application.Features.ShipmentLabels;
```
to:
```csharp
using Anela.Heblo.Application.Features.ShoptetOrders.Contracts;
```

Change line 14 from:
```csharp
    private readonly IShipmentClient _shipmentClient;
```
to:
```csharp
    private readonly IShipmentDeliveryChecker _shipmentClient;
```

Change line 31 (constructor parameter) from:
```csharp
        IShipmentClient shipmentClient,
```
to:
```csharp
        IShipmentDeliveryChecker shipmentClient,
```

No other line changes — the field assignment (`_shipmentClient = shipmentClient;`), the call site at line 99 (`_shipmentClient.HasDeliveredShipmentAsync(order.Code, cancellationToken)`), and `Metadata` are untouched.

**Step 3 — run the job's test suite and confirm all 9 tests pass.**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CompleteDeliveredOrdersJobTests"
```

Expect 9 passing tests: `ExecuteAsync_SkipsWork_WhenJobDisabled`, `ExecuteAsync_CompletesOrder_WhenShipmentDelivered`, `ExecuteAsync_AppendsNote_PreservingExistingRemark`, `ExecuteAsync_DoesNotComplete_WhenNoShipmentDelivered`, `ExecuteAsync_ProcessesBothSourceStates`, `ExecuteAsync_DryRun_DetectsButDoesNotMutate_WhenFeatureFlagDisabled`, `ExecuteAsync_UsesTestSourceState_WhenTestSourceFlagEnabled`, `ExecuteAsync_TestSourceRespectsDryRun_WhenCompletionFlagDisabled`, `ExecuteAsync_ContinuesProcessing_WhenOneOrderThrows`.

**Step 4 — build and run the full test suite.**

```bash
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

**Step 5 — commit.**

```bash
git add backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Infrastructure/Jobs/CompleteDeliveredOrdersJob.cs \
        backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/CompleteDeliveredOrdersJobTests.cs
git commit -m "Swap CompleteDeliveredOrdersJob to consumer-owned IShipmentDeliveryChecker"
```

---

