### task: packaging-handlers

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/CompletePackingOrder/CompletePackingOrderHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderHandlerTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Application/Packaging/CompletePackingOrderHandlerTests.cs`

- [ ] **Step 1: Update the failing (soon-to-fail) tests first — ScanPackingOrderHandlerTests.cs**

In `backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderHandlerTests.cs`:

- Replace the `using Anela.Heblo.Application.Features.ShoptetOrders;` line (line 4) with:
  `using Anela.Heblo.Application.Features.Packaging.Contracts;`
- Replace line 19:
  ```csharp
  private readonly Mock<IEshopOrderClient> _eshopOrderClient = new();
  ```
  with:
  ```csharp
  private readonly Mock<IPackedOrderStatusUpdater> _packedOrderStatusUpdater = new();
  ```
- Replace line 32 (`_eshopOrderClient.Object,` inside `CreateHandler()`) with:
  ```csharp
  _packedOrderStatusUpdater.Object,
  ```
- Replace every remaining `_eshopOrderClient.Verify(c => c.MarkAsPackedAsync(...` call (lines 141-143, 282-284, 315-317, 362-364, 440-442) with `_packedOrderStatusUpdater.Verify(c => c.MarkAsPackedAsync(...` — same arguments, same `Times.*`, only the mock field name changes.
- Replace the `_eshopOrderClient.Setup(c => c.MarkAsPackedAsync("0001234", It.IsAny<CancellationToken>())).ThrowsAsync(...)` call at lines 334-336 with `_packedOrderStatusUpdater.Setup(...)` — same body.

Run: `cd backend && dotnet build test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: FAIL — `ScanPackingOrderHandler`'s constructor still expects `IEshopOrderClient` as its 3rd parameter, not `IPackedOrderStatusUpdater`; compile error at `CreateHandler()`.

- [ ] **Step 2: Update ScanPackingOrderHandler.cs**

In `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs`:

- Add `using Anela.Heblo.Application.Features.Packaging.Contracts;` to the using block (after line 1).
- Remove `using Anela.Heblo.Application.Features.ShoptetOrders;` (line 3) — no longer needed once the field below is retyped.
- Replace line 17:
  ```csharp
  private readonly IEshopOrderClient _eshopOrderClient;
  ```
  with:
  ```csharp
  private readonly IPackedOrderStatusUpdater _packedOrderStatusUpdater;
  ```
- Replace the constructor parameter on line 27:
  ```csharp
  IEshopOrderClient eshopOrderClient,
  ```
  with:
  ```csharp
  IPackedOrderStatusUpdater packedOrderStatusUpdater,
  ```
- Replace line 36 (`_eshopOrderClient = eshopOrderClient;`) with:
  ```csharp
  _packedOrderStatusUpdater = packedOrderStatusUpdater;
  ```
- In `TryMarkAsPackedAsync` (line 164), replace:
  ```csharp
  await _eshopOrderClient.MarkAsPackedAsync(orderCode, ct);
  ```
  with:
  ```csharp
  await _packedOrderStatusUpdater.MarkAsPackedAsync(orderCode, ct);
  ```

- [ ] **Step 3: Update CompletePackingOrderHandler.cs**

In `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/CompletePackingOrder/CompletePackingOrderHandler.cs`:

- Replace `using Anela.Heblo.Application.Features.ShoptetOrders;` (line 2) with:
  `using Anela.Heblo.Application.Features.Packaging.Contracts;`
- Replace line 11:
  ```csharp
  private readonly IEshopOrderClient _eshopOrderClient;
  ```
  with:
  ```csharp
  private readonly IPackedOrderStatusUpdater _packedOrderStatusUpdater;
  ```
- Replace the constructor (lines 14-20):
  ```csharp
  public CompletePackingOrderHandler(
      IPackedOrderStatusUpdater packedOrderStatusUpdater,
      ILogger<CompletePackingOrderHandler> logger)
  {
      _packedOrderStatusUpdater = packedOrderStatusUpdater;
      _logger = logger;
  }
  ```
- In `Handle` (line 28), replace:
  ```csharp
  await _eshopOrderClient.MarkAsPackedAsync(request.OrderCode, cancellationToken);
  ```
  with:
  ```csharp
  await _packedOrderStatusUpdater.MarkAsPackedAsync(request.OrderCode, cancellationToken);
  ```

- [ ] **Step 4: Update CompletePackingOrderHandlerTests.cs**

Replace `using Anela.Heblo.Application.Features.ShoptetOrders;` (line 2) with:
`using Anela.Heblo.Application.Features.Packaging.Contracts;`

Replace line 12:
```csharp
private readonly Mock<IPackedOrderStatusUpdater> _packedOrderStatusUpdater = new();
```

Replace line 14-15:
```csharp
private CompletePackingOrderHandler CreateHandler() =>
    new(_packedOrderStatusUpdater.Object, new Mock<ILogger<CompletePackingOrderHandler>>().Object);
```

Replace both `_eshopOrderClient.Verify(...)` / `_eshopOrderClient.Setup(...)` occurrences (lines 26-28, 34-36) with `_packedOrderStatusUpdater.Verify(...)` / `_packedOrderStatusUpdater.Setup(...)` — same arguments.

- [ ] **Step 5: Run tests to verify they pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ScanPackingOrderHandlerTests|FullyQualifiedName~CompletePackingOrderHandlerTests"`
Expected: PASS (all tests in both fixtures, including `Handle_MarkAsPackedFails_StillReturnsSuccessfulScanResponse` and `Handle_WhenMarkAsPackedThrows_ReturnsPackingCompletionFailed`, which prove the failure-propagation behavior survived the swap).

- [ ] **Step 6: Full solution build to catch any other reference**

Run: `cd backend && dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs \
        backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/CompletePackingOrder/CompletePackingOrderHandler.cs \
        backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Application/Packaging/CompletePackingOrderHandlerTests.cs
git commit -m "refactor(packaging): consume IPackedOrderStatusUpdater instead of IEshopOrderClient"
```

---
