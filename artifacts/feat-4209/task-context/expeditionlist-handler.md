### task: expeditionlist-handler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/PrintExpeditionOrder/PrintExpeditionOrderHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/PrintExpeditionOrderHandlerTests.cs`

- [ ] **Step 1: Update the tests first**

In `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/PrintExpeditionOrderHandlerTests.cs`:

- Remove `using Anela.Heblo.Application.Features.ShoptetOrders;` (line 5) — no longer needed.
- Replace line 17:
  ```csharp
  private readonly Mock<IOrderStatusReader> _client = new();
  ```
  (the `using Anela.Heblo.Application.Features.ExpeditionList.Contracts;` import on line 2 already covers the new type — no import change needed there.)
- No other line needs to change: every `_client.Setup(c => c.GetOrderStatusIdAsync(...))` call (lines 32-33, 47-48, 68-69, 83-84, 96-97, 116-117, 128-129) keeps working unchanged because `IOrderStatusReader.GetOrderStatusIdAsync` has the identical signature to `IEshopOrderClient.GetOrderStatusIdAsync`.

Run: `cd backend && dotnet build test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: FAIL — `PrintExpeditionOrderHandler`'s constructor still expects `IEshopOrderClient` as its 2nd parameter, not `IOrderStatusReader`; compile error at `CreateHandler()`.

- [ ] **Step 2: Update PrintExpeditionOrderHandler.cs**

In `backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/PrintExpeditionOrder/PrintExpeditionOrderHandler.cs`:

- Remove `using Anela.Heblo.Application.Features.ShoptetOrders;` (line 3) — the `using Anela.Heblo.Application.Features.ExpeditionList.Contracts;` on line 1 already covers the new type.
- Replace line 24:
  ```csharp
  private readonly IOrderStatusReader _orderStatusReader;
  ```
- Replace the constructor parameter on line 30:
  ```csharp
  IOrderStatusReader orderStatusReader,
  ```
- Replace line 35 (`_eshopOrderClient = eshopOrderClient;`) with:
  ```csharp
  _orderStatusReader = orderStatusReader;
  ```
- In `Handle` (line 47), replace:
  ```csharp
  currentStatusId = await _eshopOrderClient.GetOrderStatusIdAsync(request.OrderCode, cancellationToken);
  ```
  with:
  ```csharp
  currentStatusId = await _orderStatusReader.GetOrderStatusIdAsync(request.OrderCode, cancellationToken);
  ```

The existing `catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)`
block (lines 49-55) is untouched — it already works against whatever throws from the call above, and the
adapter's `expeditionlist-adapter` task test already proves the 404 shape survives the adapter unmodified.

- [ ] **Step 3: Run tests to verify they pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PrintExpeditionOrderHandlerTests"`
Expected: PASS (all 6 test methods, including `Handle_OrderLookupReturns404_ReturnsNotFoundError` and `Handle_OrderLookupReturns500_PropagatesException`, proving the 404/500 handling paths are unaffected by the swap).

- [ ] **Step 4: Full solution build to catch any other reference**

Run: `cd backend && dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/PrintExpeditionOrder/PrintExpeditionOrderHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/ExpeditionList/PrintExpeditionOrderHandlerTests.cs
git commit -m "refactor(expedition-list): consume IOrderStatusReader instead of IEshopOrderClient"
```

---
