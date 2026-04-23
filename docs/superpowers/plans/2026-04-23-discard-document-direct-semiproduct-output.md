# Discard Document for Direct Semiproduct Output — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Automatically create a V-VYDEJ-POLOTOVAR (semiproduct outbound) document in Abra Flexi when a manufacture order includes direct semiproduct output, removing the unconsumed semiproduct from warehouse 20.

**Architecture:** The direct semiproduct output amount is threaded through the existing request/response DTOs (`SubmitManufactureRequest` -> `SubmitManufactureClientRequest`) and a new "Phase 4" is added to `FlexiManufactureClient.SubmitManufacturePerProductAsync` that calls a new `SubmitDirectSemiProductOutputAsync` method on the document service. The resulting doc code is persisted to the existing `ManufactureOrder.ErpDiscardResidueDocumentNumber` field (no DB migration needed).

**Tech Stack:** .NET 8, MediatR, Moq, xUnit, FluentAssertions, Rem.FlexiBeeSDK

---

## File Structure

| File | Action | Responsibility |
|------|--------|----------------|
| `backend/src/Anela.Heblo.Domain/Features/Manufacture/SubmitManufactureClientRequest.cs` | MODIFY | Add 3 direct output properties |
| `backend/src/Anela.Heblo.Domain/Features/Manufacture/SubmitManufactureClientResponse.cs` | MODIFY | Add `DirectSemiProductOutputDocCode` |
| `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufacture/SubmitManufactureRequest.cs` | MODIFY | Add 3 direct output properties |
| `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufacture/SubmitManufactureResponse.cs` | MODIFY | Add `DirectSemiProductOutputDocCode` |
| `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufacture/SubmitManufactureMapping.cs` | MODIFY | Map 3 new fields in `ToClientRequest()` |
| `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufacture/SubmitManufactureHandler.cs` | MODIFY | Forward + persist new doc code |
| `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/ConfirmProductCompletionWorkflow.cs` | MODIFY | Extract direct output amount, pass in submit request |
| `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/IFlexiManufactureDocumentService.cs` | MODIFY | Add `SubmitDirectSemiProductOutputAsync` |
| `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/FlexiManufactureDocumentService.cs` | MODIFY | Implement discard doc creation |
| `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureClient.cs` | MODIFY | Call Phase 4 after Phase 3 |
| `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/FlexiManufactureClientTests.cs` | MODIFY | Add tests for Phase 4 |
| `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/Workflows/ConfirmProductCompletionWorkflowTests.cs` | MODIFY | Add test verifying direct output fields on submit request |

---

### Task 1: Add direct output fields to domain request DTO

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Manufacture/SubmitManufactureClientRequest.cs:14-15`

- [ ] **Step 1: Add properties to SubmitManufactureClientRequest**

Open `backend/src/Anela.Heblo.Domain/Features/Manufacture/SubmitManufactureClientRequest.cs` and add after the `ResidueDistribution` property (line 15):

```csharp
    // Direct semiproduct output: bulk semiproduct sold as-is, needs a discard document
    public string? DirectSemiProductOutputCode { get; set; }
    public string? DirectSemiProductOutputName { get; set; }
    public decimal DirectSemiProductOutputAmount { get; set; }
```

- [ ] **Step 2: Run build to verify compilation**

Run: `dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Manufacture/SubmitManufactureClientRequest.cs
git commit -m "feat: add direct semiproduct output fields to SubmitManufactureClientRequest"
```

---

### Task 2: Add direct output doc code to domain response DTO

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Manufacture/SubmitManufactureClientResponse.cs:10`

- [ ] **Step 1: Add property to SubmitManufactureClientResponse**

Open `backend/src/Anela.Heblo.Domain/Features/Manufacture/SubmitManufactureClientResponse.cs` and add after `ProductReceiptDocCode` (line 10):

```csharp
    public string? DirectSemiProductOutputDocCode { get; set; }
```

- [ ] **Step 2: Run build**

Run: `dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Manufacture/SubmitManufactureClientResponse.cs
git commit -m "feat: add DirectSemiProductOutputDocCode to SubmitManufactureClientResponse"
```

---

### Task 3: Add direct output fields to application-layer DTOs and mapping

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufacture/SubmitManufactureRequest.cs:21`
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufacture/SubmitManufactureResponse.cs:13`
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufacture/SubmitManufactureMapping.cs:24`

- [ ] **Step 1: Add properties to SubmitManufactureRequest**

Open `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufacture/SubmitManufactureRequest.cs` and add after line 21 (`public ResidueDistribution? ResidueDistribution { get; set; }`):

```csharp
    public string? DirectSemiProductOutputCode { get; set; }
    public string? DirectSemiProductOutputName { get; set; }
    public decimal DirectSemiProductOutputAmount { get; set; }
```

- [ ] **Step 2: Add property to SubmitManufactureResponse**

Open `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufacture/SubmitManufactureResponse.cs` and add after `ProductReceiptDocCode` (line 13):

```csharp
    public string? DirectSemiProductOutputDocCode { get; set; }
```

- [ ] **Step 3: Map the three new fields in ToClientRequest**

Open `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufacture/SubmitManufactureMapping.cs`. After line 24 (`ResidueDistribution = request.ResidueDistribution,`), add:

```csharp
            DirectSemiProductOutputCode = request.DirectSemiProductOutputCode,
            DirectSemiProductOutputName = request.DirectSemiProductOutputName,
            DirectSemiProductOutputAmount = request.DirectSemiProductOutputAmount,
```

- [ ] **Step 4: Run build**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufacture/SubmitManufactureRequest.cs \
      backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufacture/SubmitManufactureResponse.cs \
      backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufacture/SubmitManufactureMapping.cs
git commit -m "feat: add direct semiproduct output fields to application-layer DTOs and mapping"
```

---

### Task 4: Add SubmitDirectSemiProductOutputAsync to document service interface

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/IFlexiManufactureDocumentService.cs:28`

- [ ] **Step 1: Add method to interface**

Open `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/IFlexiManufactureDocumentService.cs` and add before the closing brace (after line 28):

```csharp

    // Direct semiproduct output discard
    Task<string?> SubmitDirectSemiProductOutputAsync(
        SubmitManufactureClientRequest request,
        CancellationToken cancellationToken);
```

- [ ] **Step 2: Run build (will fail — implementation missing, that's expected)**

Run: `dotnet build backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Anela.Heblo.Adapters.Flexi.csproj`
Expected: FAIL — `FlexiManufactureDocumentService` does not implement `SubmitDirectSemiProductOutputAsync`

- [ ] **Step 3: Commit (interface only)**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/IFlexiManufactureDocumentService.cs
git commit -m "feat: add SubmitDirectSemiProductOutputAsync to IFlexiManufactureDocumentService"
```

---

### Task 5: Implement SubmitDirectSemiProductOutputAsync in document service

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/FlexiManufactureDocumentService.cs:328`

- [ ] **Step 1: Write the failing test**

Open `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/FlexiManufactureClientTests.cs` and add the following test inside the class, before the `#region Helper Methods`:

```csharp
    #region Direct Semiproduct Output Tests

    [Fact]
    public async Task SubmitManufactureAsync_WithDirectSemiproductOutput_CreatesDiscardDocument()
    {
        // Arrange — request has a single product plus direct semiproduct output
        var request = ManufactureTestData.CreateManufactureRequest(
            ManufactureTestData.Products.ConfidentBar, 10m);
        request.ManufactureType = ErpManufactureType.Product;
        request.DirectSemiProductOutputCode = ManufactureTestData.SemiProducts.SilkBar.Code;
        request.DirectSemiProductOutputName = ManufactureTestData.SemiProducts.SilkBar.Name;
        request.DirectSemiProductOutputAmount = 500m;

        SetupSuccessfulManufacture(
            ManufactureTestData.Products.ConfidentBar,
            ManufactureTestData.Materials.Bisabolol, 5.0);

        // Act
        var result = await _client.SubmitManufactureAsync(request);

        // Assert — 3 stock movements: 1 consumption + 1 production + 1 discard
        VerifyStockMovementsCreated(times: 3);

        // Verify the discard document is V-VYDEJ-POLOTOVAR from warehouse 20
        _mockStockMovementClient.Verify(x => x.SaveAsync(
            It.Is<StockItemsMovementUpsertRequestFlexiDto>(req =>
                req.DocumentTypeCode == "V-VYDEJ-POLOTOVAR" &&
                req.StockMovementDirection == StockMovementDirection.Out &&
                req.WarehouseId == FlexiStockClient.SemiProductsWarehouseId.ToString() &&
                req.StockItems.Count == 1 &&
                req.StockItems[0].ProductCode == ManufactureTestData.SemiProducts.SilkBar.Code &&
                req.StockItems[0].Amount == 500.0 &&
                req.StockItems[0].LotNumber == "LOT123"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitManufactureAsync_WithoutDirectSemiproductOutput_DoesNotCreateDiscardDocument()
    {
        // Arrange — no direct output
        var request = ManufactureTestData.CreateManufactureRequest(
            ManufactureTestData.Products.ConfidentBar, 10m);
        request.ManufactureType = ErpManufactureType.Product;

        SetupSuccessfulManufacture(
            ManufactureTestData.Products.ConfidentBar,
            ManufactureTestData.Materials.Bisabolol, 5.0);

        // Act
        var result = await _client.SubmitManufactureAsync(request);

        // Assert — only 2 stock movements: 1 consumption + 1 production (no discard)
        VerifyStockMovementsCreated(times: 2);
        result.DirectSemiProductOutputDocCode.Should().BeNull();
    }

    [Fact]
    public async Task SubmitManufactureAsync_WithDirectSemiproductOutput_CapturesDocCode()
    {
        // Arrange
        var request = ManufactureTestData.CreateManufactureRequest(
            ManufactureTestData.Products.ConfidentBar, 10m);
        request.ManufactureType = ErpManufactureType.Product;
        request.DirectSemiProductOutputCode = ManufactureTestData.SemiProducts.SilkBar.Code;
        request.DirectSemiProductOutputName = ManufactureTestData.SemiProducts.SilkBar.Name;
        request.DirectSemiProductOutputAmount = 500m;

        SetupSuccessfulManufacture(
            ManufactureTestData.Products.ConfidentBar,
            ManufactureTestData.Materials.Bisabolol, 5.0);
        SetupStockMovementsWithDocCodes();

        // Act
        var result = await _client.SubmitManufactureAsync(request);

        // Assert — the discard doc code should be V-VYDEJ-POLOTOVAR's code
        result.DirectSemiProductOutputDocCode.Should().Be("DOC-SP-OUT-001");
    }

    #endregion
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/test/Anela.Heblo.Adapters.Flexi.Tests/ --filter "DirectSemiproductOutput" -v n`
Expected: FAIL — build error, `SubmitDirectSemiProductOutputAsync` not implemented

- [ ] **Step 3: Implement SubmitDirectSemiProductOutputAsync**

Open `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/FlexiManufactureDocumentService.cs` and add the following method before `GetProductionDocumentType` (before line 310):

```csharp
    public async Task<string?> SubmitDirectSemiProductOutputAsync(
        SubmitManufactureClientRequest request,
        CancellationToken cancellationToken)
    {
        var warehouseId = FlexiStockClient.SemiProductsWarehouseId;
        var stockItems = await _stockClient.StockToDateAsync(request.Date, warehouseId, cancellationToken);
        var stockItem = stockItems.FirstOrDefault(s => s.ProductCode == request.DirectSemiProductOutputCode);
        var unitPrice = stockItem != null ? (double)stockItem.Price : 0;

        var amount = Math.Round((double)request.DirectSemiProductOutputAmount, 4);

        var movementItem = new StockItemsMovementUpsertRequestItemFlexiDto
        {
            ProductCode = request.DirectSemiProductOutputCode!,
            ProductName = request.DirectSemiProductOutputName ?? request.DirectSemiProductOutputCode!,
            Amount = amount,
            AmountIssued = amount,
            LotNumber = request.LotNumber,
            Expiration = request.ExpirationDate?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            UnitPrice = unitPrice,
        };

        var discardRequest = new StockItemsMovementUpsertRequestFlexiDto
        {
            CreatedBy = request.CreatedBy,
            AccountingDate = request.Date,
            IssueDate = request.Date,
            StockItems = new List<StockItemsMovementUpsertRequestItemFlexiDto> { movementItem },
            Description = request.ManufactureInternalNumber,
            DocumentTypeCode = WarehouseDocumentType_OutboundSemiProduct,
            StockMovementDirection = StockMovementDirection.Out,
            Note = request.ManufactureOrderCode,
            WarehouseId = warehouseId.ToString(),
        };

        var result = await _stockMovementClient.SaveAsync(discardRequest, cancellationToken);

        if (result != null && !result.IsSuccess)
        {
            throw new FlexiManufactureException(
                FlexiManufactureOperationKind.ConsumptionMovement,
                "Failed to create discard stock movement for direct semiproduct output",
                warehouseId: warehouseId,
                rawFlexiError: result.GetErrorMessage());
        }

        return result?.Result?.Results?.FirstOrDefault()?.Code;
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Adapters.Flexi.Tests/ --filter "DirectSemiproductOutput" -v n`
Expected: Still FAIL — `FlexiManufactureClient` doesn't call the new method yet. The doc code tests will fail. Move to Task 6.

- [ ] **Step 5: Commit (implementation)**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/FlexiManufactureDocumentService.cs \
      backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/FlexiManufactureClientTests.cs
git commit -m "feat: implement SubmitDirectSemiProductOutputAsync in FlexiManufactureDocumentService"
```

---

### Task 6: Call Phase 4 from FlexiManufactureClient

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureClient.cs:158-167`

- [ ] **Step 1: Add Phase 4 to SubmitManufacturePerProductAsync**

Open `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureClient.cs`. After Phase 3 (line 158, `var productReceiptDocCode = ...`), add:

```csharp

        // Phase 4: Create discard document for direct semiproduct output (if any)
        string? directOutputDocCode = null;
        if (request.DirectSemiProductOutputAmount > 0
            && !string.IsNullOrEmpty(request.DirectSemiProductOutputCode))
        {
            directOutputDocCode = await _documentService.SubmitDirectSemiProductOutputAsync(
                request, cancellationToken);
        }
```

Then update the return statement (currently lines 160-167) to include the new doc code:

```csharp
        return new SubmitManufactureClientResponse
        {
            ManufactureId = request.ManufactureOrderCode,
            SemiProductIssueForProductDocCode = consumptionCodes.SemiProductIssueCode,
            MaterialIssueForProductDocCode = consumptionCodes.MaterialIssueCode,
            ProductReceiptDocCode = productReceiptDocCode,
            DirectSemiProductOutputDocCode = directOutputDocCode,
        };
```

- [ ] **Step 2: Run tests**

Run: `dotnet test backend/test/Anela.Heblo.Adapters.Flexi.Tests/ --filter "DirectSemiproductOutput" -v n`
Expected: ALL 3 tests PASS

- [ ] **Step 3: Run full adapter test suite**

Run: `dotnet test backend/test/Anela.Heblo.Adapters.Flexi.Tests/ -v n`
Expected: All tests pass (no regressions)

- [ ] **Step 4: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureClient.cs
git commit -m "feat: call Phase 4 discard document creation from FlexiManufactureClient"
```

---

### Task 7: Forward and persist the doc code in SubmitManufactureHandler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufacture/SubmitManufactureHandler.cs:59-67,87-134`

- [ ] **Step 1: Forward DirectSemiProductOutputDocCode in Handle() response**

Open `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufacture/SubmitManufactureHandler.cs`. In the `Handle` method return block (lines 59-67), add after `ProductReceiptDocCode` (line 66):

```csharp
                DirectSemiProductOutputDocCode = clientResponse.DirectSemiProductOutputDocCode,
```

- [ ] **Step 2: Persist doc code in PersistDocCodesAsync**

In the same file, in `PersistDocCodesAsync` (after line 132, the `ProductReceiptDocCode` block), add:

```csharp

        if (clientResponse.DirectSemiProductOutputDocCode != null)
        {
            order.ErpDiscardResidueDocumentNumber = clientResponse.DirectSemiProductOutputDocCode;
            order.ErpDiscardResidueDocumentNumberDate = now;
        }
```

- [ ] **Step 3: Run build**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufacture/SubmitManufactureHandler.cs
git commit -m "feat: forward and persist DirectSemiProductOutputDocCode in SubmitManufactureHandler"
```

---

### Task 8: Pass direct output info from ConfirmProductCompletionWorkflow

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/ConfirmProductCompletionWorkflow.cs:148-177`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/Workflows/ConfirmProductCompletionWorkflowTests.cs`

- [ ] **Step 1: Write the failing test**

Open `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/Workflows/ConfirmProductCompletionWorkflowTests.cs`. Add the following test after the existing `ExecuteAsync_WithDirectSemiproductRow_FiltersItFromErpItems` test (after line 535):

```csharp
    [Fact]
    public async Task ExecuteAsync_WithDirectSemiproductRow_PassesDirectOutputFieldsInSubmitRequest()
    {
        // Arrange — order has product rows plus a direct semiproduct row (SP001001 = 200g)
        var productQuantities = new Dictionary<int, decimal> { { 1, 5.0m }, { 2, 200.0m } };
        var distribution = CreateDistributionWithinThreshold();
        var updateStatusResponse = CreateSuccessfulUpdateStatusResponse();

        var updateOrderResponse = new UpdateManufactureOrderResponse
        {
            Success = true,
            Order = new UpdateManufactureOrderDto
            {
                OrderNumber = "MO-2024-DIRECT2",
                SemiProduct = new UpdateManufactureOrderSemiProductDto
                {
                    ProductCode = "SP001001",
                    ProductName = "Semi Product 1",
                    ActualQuantity = 1000m,
                    PlannedQuantity = 1000m,
                    LotNumber = "LOT-DIRECT",
                    ExpirationDate = DateOnly.FromDateTime(DateTime.Today.AddDays(30)),
                },
                Products = new List<UpdateManufactureOrderProductDto>
                {
                    new UpdateManufactureOrderProductDto
                    {
                        ProductCode = "P001",
                        ProductName = "Product 1",
                        ActualQuantity = 5.0m,
                        PlannedQuantity = 5.0m,
                    },
                    new UpdateManufactureOrderProductDto
                    {
                        ProductCode = "SP001001",
                        ProductName = "Semi Product 1",
                        ActualQuantity = 200.0m,
                        PlannedQuantity = 200.0m,
                    },
                },
            },
        };

        SubmitManufactureRequest? capturedSubmitRequest = null;

        _mediatorMock
            .Setup(x => x.Send(It.IsAny<UpdateManufactureOrderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updateOrderResponse);

        _mediatorMock
            .Setup(x => x.Send(It.IsAny<SubmitManufactureRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<SubmitManufactureResponse>, CancellationToken>(
                (r, _) => capturedSubmitRequest = (SubmitManufactureRequest)r)
            .ReturnsAsync(CreateSuccessfulSubmitManufactureResponse("ERP-DIRECT-002"));

        _mediatorMock
            .Setup(x => x.Send(It.IsAny<UpdateManufactureOrderStatusRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updateStatusResponse);

        _mediatorMock
            .Setup(x => x.Send(It.IsAny<UpdateBoMIngredientAmountRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateBoMIngredientAmountResponse());

        _residueCalculatorMock
            .Setup(x => x.CalculateAsync(It.IsAny<UpdateManufactureOrderDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(distribution);

        // Act
        var result = await _workflow.ExecuteAsync(ValidOrderId, productQuantities, false, ValidChangeReason, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        capturedSubmitRequest.Should().NotBeNull();
        capturedSubmitRequest!.DirectSemiProductOutputCode.Should().Be("SP001001");
        capturedSubmitRequest.DirectSemiProductOutputName.Should().Be("Semi Product 1");
        capturedSubmitRequest.DirectSemiProductOutputAmount.Should().Be(200.0m);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutDirectSemiproductRow_DirectOutputAmountIsZero()
    {
        // Arrange — no direct semiproduct row
        var productQuantities = new Dictionary<int, decimal> { { 1, 5.0m } };
        var updateOrderResponse = CreateSuccessfulUpdateOrderResponse();
        var submitManufactureResponse = CreateSuccessfulSubmitManufactureResponse("ERP-NODIRECT");
        var updateStatusResponse = CreateSuccessfulUpdateStatusResponse();
        var distribution = CreateDistributionWithinThreshold();

        SubmitManufactureRequest? capturedSubmitRequest = null;

        SetupMediatorResponses(updateOrderResponse, submitManufactureResponse, updateStatusResponse);

        _mediatorMock
            .Setup(x => x.Send(It.IsAny<SubmitManufactureRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<SubmitManufactureResponse>, CancellationToken>(
                (r, _) => capturedSubmitRequest = (SubmitManufactureRequest)r)
            .ReturnsAsync(submitManufactureResponse);

        _residueCalculatorMock
            .Setup(x => x.CalculateAsync(It.IsAny<UpdateManufactureOrderDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(distribution);

        // Act
        var result = await _workflow.ExecuteAsync(ValidOrderId, productQuantities, false, ValidChangeReason, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        capturedSubmitRequest.Should().NotBeNull();
        capturedSubmitRequest!.DirectSemiProductOutputAmount.Should().Be(0m);
        capturedSubmitRequest.DirectSemiProductOutputCode.Should().BeNull();
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests/ --filter "DirectOutput" -v n`
Expected: FAIL — `DirectSemiProductOutputCode` is always null (workflow doesn't set it yet)

- [ ] **Step 3: Implement the workflow changes**

Open `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/ConfirmProductCompletionWorkflow.cs`. In `SubmitToErpAsync`, replace lines 152-177 with:

```csharp
        // Calculate direct semiproduct output total (rows where ProductCode == SemiProduct.ProductCode)
        var directOutputTotal = order.Products
            .Where(p => p.ProductCode == semiProduct.ProductCode)
            .Sum(p => p.ActualQuantity ?? p.PlannedQuantity);

        // Exclude direct semiproduct output rows from the ERP submission.
        // These rows (ProductCode == SemiProduct.ProductCode) represent bulk semiproduct
        // sold as-is and should not be reported as finished product output to the ERP.
        var items = order.Products
            .Where(p => p.ProductCode != semiProduct.ProductCode)
            .Select(p => new SubmitManufactureRequestItem
            {
                ProductCode = p.ProductCode,
                Name = p.ProductName,
                Amount = p.ActualQuantity ?? p.PlannedQuantity,
            })
            .ToList();

        var submitRequest = new SubmitManufactureRequest
        {
            ManufactureOrderId = orderId,
            ManufactureOrderNumber = order.OrderNumber,
            ManufactureInternalNumber = manufactureName,
            ManufactureType = ErpManufactureType.Product,
            Date = _timeProvider.GetUtcNow().DateTime,
            CreatedBy = _currentUserService.GetCurrentUser().Name,
            Items = items,
            LotNumber = semiProduct.LotNumber,
            ExpirationDate = semiProduct.ExpirationDate,
            ResidueDistribution = distribution,
            DirectSemiProductOutputCode = directOutputTotal > 0 ? semiProduct.ProductCode : null,
            DirectSemiProductOutputName = directOutputTotal > 0 ? semiProduct.ProductName : null,
            DirectSemiProductOutputAmount = directOutputTotal,
        };
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/ --filter "DirectOutput|DirectSemiproduct" -v n`
Expected: ALL PASS

- [ ] **Step 5: Run full workflow test suite**

Run: `dotnet test backend/test/Anela.Heblo.Tests/ --filter "ConfirmProductCompletionWorkflow" -v n`
Expected: All tests pass (no regressions)

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/ConfirmProductCompletionWorkflow.cs \
      backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/Workflows/ConfirmProductCompletionWorkflowTests.cs
git commit -m "feat: pass direct semiproduct output info from ConfirmProductCompletionWorkflow to submit request"
```

---

### Task 9: Full integration build and test verification

**Files:**
- No new changes — verification only

- [ ] **Step 1: Full solution build**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: Build succeeded, 0 errors

- [ ] **Step 2: Run all tests**

Run: `dotnet test backend/Anela.Heblo.sln -v n`
Expected: All tests pass

- [ ] **Step 3: Format check**

Run: `dotnet format backend/Anela.Heblo.sln --verify-no-changes`
Expected: No formatting issues (if there are, run `dotnet format backend/Anela.Heblo.sln` to fix and commit)

---

## Validation Commands

### Build
```bash
dotnet build backend/Anela.Heblo.sln
```
EXPECT: Build succeeded, 0 errors

### Unit Tests
```bash
dotnet test backend/Anela.Heblo.sln -v n
```
EXPECT: All tests pass, no regressions

### Targeted Tests
```bash
dotnet test backend/test/Anela.Heblo.Adapters.Flexi.Tests/ --filter "DirectSemiproductOutput" -v n
dotnet test backend/test/Anela.Heblo.Tests/ --filter "DirectOutput|DirectSemiproduct" -v n
```
EXPECT: All 5 new tests pass

### Format
```bash
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```
EXPECT: No changes needed

### Manual Validation
- [ ] Create a manufacture order with direct semiproduct output (e.g. 3000g semiproduct, 500g direct output)
- [ ] Complete semiproduct manufacturing (Phase A)
- [ ] Complete product manufacturing (Phase B)
- [ ] Verify in Flexi that a V-VYDEJ-POLOTOVAR document was created for the 500g
- [ ] Verify `ErpDiscardResidueDocumentNumber` is populated on the ManufactureOrder in the database
- [ ] Verify the discard document has the correct lot number, expiration, and unit price

---

## Acceptance Criteria
- [ ] All tasks completed
- [ ] All validation commands pass
- [ ] 5 new tests written and passing
- [ ] No type errors
- [ ] No lint/format errors
- [ ] No regressions in existing tests
- [ ] `ErpDiscardResidueDocumentNumber` populated automatically (no manual entry needed)

## Risks
| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Stock price lookup returns 0 for semiproduct | Low | Medium (zero-value document in Flexi) | Same pattern as existing consumption docs — acceptable |
| Discard document fails in Flexi | Low | Medium (order stuck with ManualActionRequired) | FlexiManufactureException is caught by SubmitManufactureHandler, sets ManualActionRequired=true, existing manual resolution flow handles it |
| Two V-VYDEJ-POLOTOVAR documents created in same call (main consumption + discard) | Medium | Low | They have different line items and amounts — Flexi handles this fine, confirmed by existing pattern where multiple warehouse docs are created |

## Notes
- The `DiscardRedisueDocumentCode` field on `UpdateManufactureOrderStatusRequest` (note typo) is dead code — never read by the handler. Left as-is; cleanup in a separate PR.
- No database migration needed — `ErpDiscardResidueDocumentNumber` and `ErpDiscardResidueDocumentNumberDate` already exist on `ManufactureOrder`.
- The discard document reuses `request.LotNumber` and `request.ExpirationDate` which are the semiproduct's lot/expiration from the manufacture order — same values used by the Phase B semiproduct consumption document.
