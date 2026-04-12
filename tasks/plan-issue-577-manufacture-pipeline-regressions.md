# Issue #577 — Manufacture Pipeline Regression Fixes

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix four blocking behavioral regressions introduced by PR #527 (`refactor/manufacture-pipeline`) so the refactor can be merged safely without breaking production semi-product and product completion workflows.

**Architecture:** Apply minimal, targeted fixes directly on the `refactor/manufacture-pipeline` branch. Each fix is covered by a failing unit test first (TDD). No new abstractions, no scope creep — restore the lost behavior and add the missing assertions that would have caught each regression.

**Tech Stack:** .NET 8, xUnit + Moq + FluentAssertions, MediatR, EF Core 8, Clean Architecture with Vertical Slice.

---

## CRITICAL: Target Branch

**ALL commits must land on `refactor/manufacture-pipeline`, NOT `main`.**

```bash
git fetch origin
git checkout refactor/manufacture-pipeline
git pull --ff-only
```

---

## File Map

**Production files to modify:**
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/FlexiManufactureDocumentService.cs` — restore `Math.Round(amount, 4)` in both consumption methods
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/ConfirmSemiProductManufactureWorkflow.cs` — re-populate two `FlexiDoc*` fields in `UpdateStatusAsync`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/ConfirmProductCompletionWorkflow.cs` — re-populate three `FlexiDoc*` fields AND truncate note in `TransitionToCompletedAsync`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufacture/SubmitManufactureMapping.cs` — remove undocumented `ValidateIngredientStock = true`

**Test files to extend:**
- `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/FlexiManufactureClientTests.cs` — 2 rounding tests
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/Workflows/ConfirmSemiProductManufactureWorkflowTests.cs` — 1 FlexiDoc field test
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/Workflows/ConfirmProductCompletionWorkflowTests.cs` — 2 tests: FlexiDoc fields + note truncation
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/SubmitManufactureHandlerTests.cs` — 1 test asserting `ValidateIngredientStock == false`

---

## Task 1: Restore `Math.Round(amount, 4)` in both consumption paths

**Why:** Commit `fba995e1` (#572) added this rounding to prevent Flexi from rejecting stock movements when `decimal → double` conversion drift accumulates across FEFO lot allocations (e.g. `5800.0000000000009 > 5800.0`). The refactor lost it during merge-conflict resolution. Must be restored in **both** `SubmitConsolidatedConsumptionAsync` (product path) and `SubmitConsumptionAsync` (semi-product path).

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/FlexiManufactureDocumentService.cs`
- Test: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/FlexiManufactureClientTests.cs`

### Step 1.1: Switch to the correct branch

- [ ] **Step 1.1: Checkout target branch**

```bash
git fetch origin
git checkout refactor/manufacture-pipeline
git pull --ff-only
```

### Step 1.2: Write the first failing test (product / consolidated path)

- [ ] **Step 1.2: Add failing rounding test for product path**

Add this test method inside the `FlexiManufactureClientTests` class in `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/FlexiManufactureClientTests.cs`, after the existing `SubmitManufactureAsync_ItemsWithZeroAmount_SkipsZeroAmountItems` test (inside the `#region Basic Flow Tests` section):

```csharp
[Fact]
public async Task SubmitManufactureAsync_Product_ConsumptionAmountsRoundedToFourDecimals()
{
    // Arrange — template: 5800.0 of Bisabolol per 1 unit of ConfidentBar.
    // Requesting 1.0000000000001m units forces (decimal -> double) drift:
    // the FEFO allocator computes 5800.0 * (double)1.0000000000001m which
    // produces something like 5800.0000000000009 — more than 4 decimal places.
    // Without Math.Round, this gets posted to Flexi and gets rejected.
    SetupSuccessfulManufacture(
        ManufactureTestData.Products.ConfidentBar,
        ManufactureTestData.Materials.Bisabolol,
        5800.0);

    var request = ManufactureTestData.CreateManufactureRequest(
        ManufactureTestData.Products.ConfidentBar,
        amount: 1.0000000000001m);
    request.ManufactureType = ErpManufactureType.Product;

    // Act
    await _client.SubmitManufactureAsync(request);

    // Assert — every Out-direction stock movement must have all amounts
    // rounded to exactly 4 decimal places (amount == Math.Round(amount, 4)).
    _mockStockMovementClient.Verify(
        m => m.SaveAsync(
            It.Is<StockItemsMovementUpsertRequestFlexiDto>(req =>
                req.StockMovementDirection == StockMovementDirection.Out &&
                req.StockItems.All(i => i.Amount == Math.Round(i.Amount, 4)) &&
                req.StockItems.All(i => i.AmountIssued == Math.Round(i.AmountIssued, 4))),
            It.IsAny<CancellationToken>()),
        Times.AtLeastOnce);
}
```

### Step 1.3: Run the test and verify it fails

- [ ] **Step 1.3: Run to confirm failure**

```bash
cd backend
dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj \
  --filter "FullyQualifiedName~SubmitManufactureAsync_Product_ConsumptionAmountsRoundedToFourDecimals" \
  --no-restore
```

Expected: **FAIL** — at least one amount has more than 4 decimal places.

### Step 1.4: Write the second failing test (semi-product / aggregated path)

- [ ] **Step 1.4: Add failing rounding test for semi-product path**

Add immediately after the test from Step 1.2:

```csharp
[Fact]
public async Task SubmitManufactureAsync_SemiProduct_ConsumptionAmountsRoundedToFourDecimals()
{
    // Arrange — SemiProduct path uses SubmitConsumptionAsync (aggregated).
    SetupSuccessfulManufacture(
        ManufactureTestData.SemiProducts.SilkBar,
        ManufactureTestData.Materials.Bisabolol,
        5800.0);

    var request = ManufactureTestData.CreateManufactureRequest(
        ManufactureTestData.SemiProducts.SilkBar,
        amount: 1.0000000000001m);
    request.ManufactureType = ErpManufactureType.SemiProduct;

    // Act
    await _client.SubmitManufactureAsync(request);

    // Assert — same rounding invariant on the semi-product path.
    _mockStockMovementClient.Verify(
        m => m.SaveAsync(
            It.Is<StockItemsMovementUpsertRequestFlexiDto>(req =>
                req.StockMovementDirection == StockMovementDirection.Out &&
                req.StockItems.All(i => i.Amount == Math.Round(i.Amount, 4)) &&
                req.StockItems.All(i => i.AmountIssued == Math.Round(i.AmountIssued, 4))),
            It.IsAny<CancellationToken>()),
        Times.AtLeastOnce);
}
```

### Step 1.5: Run both tests and verify they fail

- [ ] **Step 1.5: Run both rounding tests**

```bash
cd backend
dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj \
  --filter "FullyQualifiedName~ConsumptionAmountsRoundedToFourDecimals" \
  --no-restore
```

Expected: **2 FAIL**.

### Step 1.6: Fix `SubmitConsolidatedConsumptionAsync`

- [ ] **Step 1.6: Apply fix to the consolidated (product) consumption path**

In `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/FlexiManufactureDocumentService.cs`, locate the inner `foreach` loop inside `SubmitConsolidatedConsumptionAsync`. The current code is:

```csharp
foreach (var consumptionItem in warehouseGroup)
{
    var stockItem = stockItems.FirstOrDefault(s => s.ProductCode == consumptionItem.ProductCode);
    var unitPrice = stockItem != null ? (double)stockItem.Price : 0;

    // Track cost per manufactured product
    productCosts[consumptionItem.SourceProductCode] += unitPrice * consumptionItem.Amount;

    stockMovementItems.Add(new StockItemsMovementUpsertRequestItemFlexiDto
    {
        ProductCode = consumptionItem.ProductCode,
        ProductName = consumptionItem.ProductName,
        Amount = consumptionItem.Amount,
        AmountIssued = consumptionItem.Amount,
        LotNumber = consumptionItem.LotNumber,
        Expiration = consumptionItem.Expiration?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
        UnitPrice = unitPrice,
    });
}
```

Change it to:

```csharp
foreach (var consumptionItem in warehouseGroup)
{
    var stockItem = stockItems.FirstOrDefault(s => s.ProductCode == consumptionItem.ProductCode);
    var unitPrice = stockItem != null ? (double)stockItem.Price : 0;

    // Round to 4 decimal places to eliminate double-precision drift that
    // originates from (decimal -> double) conversions accumulating across
    // FEFO lot allocations. Without this, Flexi rejects movements like
    // 5800.0000000000009 > 5800.0 available. See PR #572 / commit fba995e1.
    var amount = Math.Round(consumptionItem.Amount, 4);

    // Track cost per manufactured product
    productCosts[consumptionItem.SourceProductCode] += unitPrice * amount;

    stockMovementItems.Add(new StockItemsMovementUpsertRequestItemFlexiDto
    {
        ProductCode = consumptionItem.ProductCode,
        ProductName = consumptionItem.ProductName,
        Amount = amount,
        AmountIssued = amount,
        LotNumber = consumptionItem.LotNumber,
        Expiration = consumptionItem.Expiration?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
        UnitPrice = unitPrice,
    });
}
```

### Step 1.7: Fix `SubmitConsumptionAsync`

- [ ] **Step 1.7: Apply fix to the aggregated (semi-product) consumption path**

In the **same file**, locate the inner `foreach` loop inside `SubmitConsumptionAsync`. The current code is:

```csharp
foreach (var consumptionItem in warehouseGroup)
{
    var stockItem = stockItems.FirstOrDefault(s => s.ProductCode == consumptionItem.ProductCode);
    var unitPrice = stockItem != null ? (double)stockItem.Price : 0;
    totalConsumptionCost += unitPrice * consumptionItem.Amount;

    stockMovementItems.Add(new StockItemsMovementUpsertRequestItemFlexiDto
    {
        ProductCode = consumptionItem.ProductCode,
        ProductName = consumptionItem.ProductName,
        Amount = consumptionItem.Amount,
        AmountIssued = consumptionItem.Amount,
        LotNumber = consumptionItem.LotNumber,
        Expiration = consumptionItem.Expiration?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
        UnitPrice = unitPrice
    });
}
```

Change it to:

```csharp
foreach (var consumptionItem in warehouseGroup)
{
    var stockItem = stockItems.FirstOrDefault(s => s.ProductCode == consumptionItem.ProductCode);
    var unitPrice = stockItem != null ? (double)stockItem.Price : 0;

    // Round to 4 decimal places — see SubmitConsolidatedConsumptionAsync above.
    // Rounding must be applied to BOTH cost aggregation and the DTO payload.
    var amount = Math.Round(consumptionItem.Amount, 4);

    totalConsumptionCost += unitPrice * amount;

    stockMovementItems.Add(new StockItemsMovementUpsertRequestItemFlexiDto
    {
        ProductCode = consumptionItem.ProductCode,
        ProductName = consumptionItem.ProductName,
        Amount = amount,
        AmountIssued = amount,
        LotNumber = consumptionItem.LotNumber,
        Expiration = consumptionItem.Expiration?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
        UnitPrice = unitPrice
    });
}
```

### Step 1.8: Re-run both tests and verify they pass

- [ ] **Step 1.8: Verify fix**

```bash
cd backend
dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj \
  --filter "FullyQualifiedName~ConsumptionAmountsRoundedToFourDecimals" \
  --no-restore
```

Expected: **2 PASS**.

### Step 1.9: Run full adapter test suite

- [ ] **Step 1.9: Run full adapter tests**

```bash
cd backend
dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj --no-restore
```

Expected: all existing tests pass + 2 new.

### Step 1.10: Commit

- [ ] **Step 1.10: Commit the fix**

```bash
git add \
  backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/FlexiManufactureDocumentService.cs \
  backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/FlexiManufactureClientTests.cs

git commit -m "$(cat <<'EOF'
fix(manufacture): restore 4-decimal rounding on consumption amounts

Commit fba995e1 (#572) added Math.Round(amount, 4) to both consumption
paths to prevent Flexi from rejecting movements when double-precision
drift (e.g. 5800.0000000000009) accumulates across FEFO lot allocations.
The rounding was lost during merge-conflict resolution when main was
merged into refactor/manufacture-pipeline. Restore the fix in both
SubmitConsolidatedConsumptionAsync and SubmitConsumptionAsync, and add
regression tests in FlexiManufactureClientTests.
EOF
)"
```

---

## Task 2: Re-populate `FlexiDoc*` fields in `ConfirmSemiProductManufactureWorkflow`

**Why:** Old service forwarded `MaterialIssueForSemiProductDocCode` and `SemiProductReceiptDocCode` from the submit result into `UpdateManufactureOrderStatusRequest`. The new workflow drops them, leaving `FlexiDocMaterialIssueForSemiProduct` and `FlexiDocSemiProductReceipt` columns NULL on every newly confirmed semi-product order — breaking audit trails.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/ConfirmSemiProductManufactureWorkflow.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/Workflows/ConfirmSemiProductManufactureWorkflowTests.cs`

### Step 2.1: Write the failing test

- [ ] **Step 2.1: Add FlexiDoc forwarding test**

Append this test to the `ConfirmSemiProductManufactureWorkflowTests` class, after the existing tests and before `#region Helper Methods`:

```csharp
[Fact]
public async Task ExecuteAsync_HappyPath_ForwardsFlexiDocCodesToStatusRequest()
{
    // Arrange — submit response carries FlexiDoc codes; status request must forward them.
    var updateOrderResponse = CreateSuccessfulUpdateOrderResponse();
    var submitResponse = new SubmitManufactureResponse
    {
        Success = true,
        ManufactureId = "MFG-SEMI-001",
        MaterialIssueForSemiProductDocCode = "FLX-MI-SP-001",
        SemiProductReceiptDocCode = "FLX-RCPT-SP-001",
    };
    UpdateManufactureOrderStatusRequest? capturedStatusRequest = null;
    var updateStatusResponse = new UpdateManufactureOrderStatusResponse { Success = true };

    _mediatorMock
        .Setup(x => x.Send(It.IsAny<UpdateManufactureOrderRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(updateOrderResponse);

    _mediatorMock
        .Setup(x => x.Send(It.IsAny<SubmitManufactureRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(submitResponse);

    _mediatorMock
        .Setup(x => x.Send(It.IsAny<UpdateManufactureOrderStatusRequest>(), It.IsAny<CancellationToken>()))
        .Callback<IRequest<UpdateManufactureOrderStatusResponse>, CancellationToken>(
            (r, _) => capturedStatusRequest = (UpdateManufactureOrderStatusRequest)r)
        .ReturnsAsync(updateStatusResponse);

    // Act
    var result = await _workflow.ExecuteAsync(
        ValidOrderId, ValidQuantity, ValidChangeReason, CancellationToken.None);

    // Assert
    result.Success.Should().BeTrue();
    capturedStatusRequest.Should().NotBeNull();
    capturedStatusRequest!.FlexiDocMaterialIssueForSemiProduct.Should().Be("FLX-MI-SP-001");
    capturedStatusRequest.FlexiDocSemiProductReceipt.Should().Be("FLX-RCPT-SP-001");
    // Product-completion fields must NOT be set in semi-product workflow
    capturedStatusRequest.FlexiDocSemiProductIssueForProduct.Should().BeNull();
    capturedStatusRequest.FlexiDocMaterialIssueForProduct.Should().BeNull();
    capturedStatusRequest.FlexiDocProductReceipt.Should().BeNull();
}
```

### Step 2.2: Run the test and verify it fails

- [ ] **Step 2.2: Confirm failure**

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ConfirmSemiProductManufactureWorkflowTests.ExecuteAsync_HappyPath_ForwardsFlexiDocCodesToStatusRequest" \
  --no-restore
```

Expected: **FAIL** — both FlexiDoc assertions fail because `UpdateStatusAsync` never sets those fields.

### Step 2.3: Apply the fix

- [ ] **Step 2.3: Populate FlexiDoc fields in `UpdateStatusAsync`**

In `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/ConfirmSemiProductManufactureWorkflow.cs`, locate `UpdateStatusAsync`. The current `statusRequest` initializer is:

```csharp
var statusRequest = new UpdateManufactureOrderStatusRequest
{
    Id = orderId,
    NewState = ManufactureOrderState.SemiProductManufactured,
    ChangeReason = changeReason ?? string.Format(ManufactureMessages.SemiProductDefaultChangeReasonFormat, actualQuantity),
    Note = submitResult.Success
        ? string.Format(ManufactureMessages.SemiProductErpNoteFormat, submitResult.ManufactureId)
        : submitResult.UserMessage ?? submitResult.FullError(),
    SemiProductOrderCode = submitResult.ManufactureId,
    ProductOrderCode = null,
    DiscardRedisueDocumentCode = null,
    ManualActionRequired = !submitResult.Success,
    WeightWithinTolerance = null,
    WeightDifference = null,
};
```

Change it to:

```csharp
var statusRequest = new UpdateManufactureOrderStatusRequest
{
    Id = orderId,
    NewState = ManufactureOrderState.SemiProductManufactured,
    ChangeReason = changeReason ?? string.Format(ManufactureMessages.SemiProductDefaultChangeReasonFormat, actualQuantity),
    Note = submitResult.Success
        ? string.Format(ManufactureMessages.SemiProductErpNoteFormat, submitResult.ManufactureId)
        : submitResult.UserMessage ?? submitResult.FullError(),
    SemiProductOrderCode = submitResult.ManufactureId,
    ProductOrderCode = null,
    DiscardRedisueDocumentCode = null,
    ManualActionRequired = !submitResult.Success,
    WeightWithinTolerance = null,
    WeightDifference = null,
    // Restore Flexi sub-document codes so ManufactureOrder.FlexiDoc* columns
    // are populated. These were forwarded by the old service and must keep
    // being forwarded to preserve audit trails that link orders to Flexi docs.
    FlexiDocMaterialIssueForSemiProduct = submitResult.MaterialIssueForSemiProductDocCode,
    FlexiDocSemiProductReceipt = submitResult.SemiProductReceiptDocCode,
};
```

### Step 2.4: Verify the fix

- [ ] **Step 2.4: Run the new test**

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ConfirmSemiProductManufactureWorkflowTests.ExecuteAsync_HappyPath_ForwardsFlexiDocCodesToStatusRequest" \
  --no-restore
```

Expected: **PASS**.

### Step 2.5: Run the full workflow test class

- [ ] **Step 2.5: Full class passes**

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ConfirmSemiProductManufactureWorkflowTests" \
  --no-restore
```

Expected: all existing tests + 1 new = all PASS.

### Step 2.6: Commit

- [ ] **Step 2.6: Commit**

```bash
git add \
  backend/src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/ConfirmSemiProductManufactureWorkflow.cs \
  backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/Workflows/ConfirmSemiProductManufactureWorkflowTests.cs

git commit -m "$(cat <<'EOF'
fix(manufacture): forward FlexiDoc codes in ConfirmSemiProductManufactureWorkflow

Old ManufactureOrderApplicationService forwarded
MaterialIssueForSemiProductDocCode and SemiProductReceiptDocCode from
the submit-manufacture result into UpdateManufactureOrderStatusRequest.
The new workflow dropped them, leaving FlexiDocMaterialIssueForSemiProduct
and FlexiDocSemiProductReceipt NULL for every confirmed semi-product order.
Restore the forwarding and add a regression test.
EOF
)"
```

---

## Task 3: Re-populate `FlexiDoc*` fields in `ConfirmProductCompletionWorkflow`

**Why:** Same regression as Task 2, for the product-completion path. Old service forwarded `SemiProductIssueForProductDocCode`, `MaterialIssueForProductDocCode`, and `ProductReceiptDocCode`. New workflow drops all three.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/ConfirmProductCompletionWorkflow.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/Workflows/ConfirmProductCompletionWorkflowTests.cs`

### Step 3.1: Write the failing test

- [ ] **Step 3.1: Add FlexiDoc forwarding test for product path**

Append this test to `ConfirmProductCompletionWorkflowTests`, before `#region Helper Methods`:

```csharp
[Fact]
public async Task ExecuteAsync_HappyPath_ForwardsFlexiDocCodesToStatusRequest()
{
    // Arrange
    var productQuantities = new Dictionary<int, decimal> { { 1, 5.0m }, { 2, 3.5m } };
    var updateOrderResponse = CreateSuccessfulUpdateOrderResponse();
    var submitResponse = new SubmitManufactureResponse
    {
        Success = true,
        ManufactureId = "MFG-PROD-001",
        SemiProductIssueForProductDocCode = "FLX-SP-ISSUE-001",
        MaterialIssueForProductDocCode = "FLX-MI-PROD-001",
        ProductReceiptDocCode = "FLX-RCPT-PROD-001",
    };
    var distribution = CreateDistributionWithinThreshold();
    UpdateManufactureOrderStatusRequest? capturedStatusRequest = null;
    var updateStatusResponse = new UpdateManufactureOrderStatusResponse { Success = true };

    _mediatorMock
        .Setup(x => x.Send(It.IsAny<UpdateManufactureOrderRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(updateOrderResponse);

    _mediatorMock
        .Setup(x => x.Send(It.IsAny<SubmitManufactureRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(submitResponse);

    _mediatorMock
        .Setup(x => x.Send(It.IsAny<UpdateBoMIngredientAmountRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new UpdateBoMIngredientAmountResponse { Success = true });

    _mediatorMock
        .Setup(x => x.Send(It.IsAny<UpdateManufactureOrderStatusRequest>(), It.IsAny<CancellationToken>()))
        .Callback<IRequest<UpdateManufactureOrderStatusResponse>, CancellationToken>(
            (r, _) => capturedStatusRequest = (UpdateManufactureOrderStatusRequest)r)
        .ReturnsAsync(updateStatusResponse);

    _residueCalculatorMock
        .Setup(x => x.CalculateAsync(It.IsAny<UpdateManufactureOrderDto>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(distribution);

    // Act
    var result = await _workflow.ExecuteAsync(
        ValidOrderId, productQuantities, overrideConfirmed: false, changeReason: null, CancellationToken.None);

    // Assert
    result.Success.Should().BeTrue();
    capturedStatusRequest.Should().NotBeNull();
    capturedStatusRequest!.FlexiDocSemiProductIssueForProduct.Should().Be("FLX-SP-ISSUE-001");
    capturedStatusRequest.FlexiDocMaterialIssueForProduct.Should().Be("FLX-MI-PROD-001");
    capturedStatusRequest.FlexiDocProductReceipt.Should().Be("FLX-RCPT-PROD-001");
    // Semi-product fields must NOT be set in the product-completion workflow
    capturedStatusRequest.FlexiDocMaterialIssueForSemiProduct.Should().BeNull();
    capturedStatusRequest.FlexiDocSemiProductReceipt.Should().BeNull();
}
```

### Step 3.2: Run the test and verify it fails

- [ ] **Step 3.2: Confirm failure**

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ConfirmProductCompletionWorkflowTests.ExecuteAsync_HappyPath_ForwardsFlexiDocCodesToStatusRequest" \
  --no-restore
```

Expected: **FAIL** — all three FlexiDoc assertions fail because `TransitionToCompletedAsync` never sets them.

### Step 3.3: Apply the fix

- [ ] **Step 3.3: Populate FlexiDoc fields in `TransitionToCompletedAsync`**

In `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/ConfirmProductCompletionWorkflow.cs`, locate the `statusRequest` initializer in `TransitionToCompletedAsync`. The current code is:

```csharp
var statusRequest = new UpdateManufactureOrderStatusRequest
{
    Id = orderId,
    NewState = ManufactureOrderState.Completed,
    ChangeReason = changeReason ?? ManufactureMessages.ProductCompletionDefaultChangeReason,
    Note = combined,
    SemiProductOrderCode = null,
    ProductOrderCode = submitResult.ManufactureId,
    DiscardRedisueDocumentCode = null,
    ManualActionRequired = manualActionRequired,
    WeightWithinTolerance = distribution.IsWithinAllowedThreshold,
    WeightDifference = distribution.Difference,
};
```

Change it to:

```csharp
var statusRequest = new UpdateManufactureOrderStatusRequest
{
    Id = orderId,
    NewState = ManufactureOrderState.Completed,
    ChangeReason = changeReason ?? ManufactureMessages.ProductCompletionDefaultChangeReason,
    Note = combined,
    SemiProductOrderCode = null,
    ProductOrderCode = submitResult.ManufactureId,
    DiscardRedisueDocumentCode = null,
    ManualActionRequired = manualActionRequired,
    WeightWithinTolerance = distribution.IsWithinAllowedThreshold,
    WeightDifference = distribution.Difference,
    // Restore Flexi sub-document codes so ManufactureOrder.FlexiDoc* columns
    // are populated. These were forwarded by the old service and must keep
    // being forwarded to preserve audit trails that link orders to Flexi docs.
    FlexiDocSemiProductIssueForProduct = submitResult.SemiProductIssueForProductDocCode,
    FlexiDocMaterialIssueForProduct = submitResult.MaterialIssueForProductDocCode,
    FlexiDocProductReceipt = submitResult.ProductReceiptDocCode,
};
```

### Step 3.4: Verify the fix

- [ ] **Step 3.4: Run the new test**

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ConfirmProductCompletionWorkflowTests.ExecuteAsync_HappyPath_ForwardsFlexiDocCodesToStatusRequest" \
  --no-restore
```

Expected: **PASS**.

### Step 3.5: Run full workflow test class

- [ ] **Step 3.5: Full class passes**

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ConfirmProductCompletionWorkflowTests" \
  --no-restore
```

Expected: all existing tests + 1 new = all PASS.

### Step 3.6: Commit

- [ ] **Step 3.6: Commit**

```bash
git add \
  backend/src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/ConfirmProductCompletionWorkflow.cs \
  backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/Workflows/ConfirmProductCompletionWorkflowTests.cs

git commit -m "$(cat <<'EOF'
fix(manufacture): forward FlexiDoc codes in ConfirmProductCompletionWorkflow

Old ManufactureOrderApplicationService forwarded
SemiProductIssueForProductDocCode, MaterialIssueForProductDocCode, and
ProductReceiptDocCode from the submit-manufacture result into
UpdateManufactureOrderStatusRequest. The new workflow dropped all three,
leaving the corresponding entity columns NULL for every newly completed
product order. Restore the forwarding and add a regression test.
EOF
)"
```

---

## Task 4: Remove undocumented `ValidateIngredientStock = true`

**Why:** `SubmitManufactureMapping.ToClientRequest` silently activates client-side ingredient-stock validation on every submit. This was not in the old code (default was `false`) and was not disclosed in the PR. Combined with the rounding fix, the strict `<` validator can reject borderline precision drift. If the feature is wanted, it needs a separate PR with documentation, epsilon tolerance, and tests. For this plan: revert to the old default of `false`.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufacture/SubmitManufactureMapping.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Manufacture/SubmitManufactureHandlerTests.cs`

### Step 4.1: Write the failing test

- [ ] **Step 4.1: Add test asserting `ValidateIngredientStock == false`**

Append this test to `SubmitManufactureHandlerTests`, before the `BuildRequest` helper:

```csharp
[Fact]
public async Task Handle_DoesNotActivateIngredientStockValidation()
{
    // Arrange — ingredient stock validation must NOT be activated at the
    // client boundary by the default submit path. Activation should be an
    // explicit, documented opt-in in a separate PR.
    SubmitManufactureClientRequest? capturedClientRequest = null;
    _clientMock
        .Setup(c => c.SubmitManufactureAsync(It.IsAny<SubmitManufactureClientRequest>(), It.IsAny<CancellationToken>()))
        .Callback<SubmitManufactureClientRequest, CancellationToken>(
            (r, _) => capturedClientRequest = r)
        .ReturnsAsync(new SubmitManufactureClientResponse { ManufactureId = "MAN-VAL-1" });

    // Act
    await _handler.Handle(BuildRequest(), CancellationToken.None);

    // Assert
    capturedClientRequest.Should().NotBeNull();
    capturedClientRequest!.ValidateIngredientStock.Should().BeFalse();
}
```

### Step 4.2: Run the test and verify it fails

- [ ] **Step 4.2: Confirm failure**

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~SubmitManufactureHandlerTests.Handle_DoesNotActivateIngredientStockValidation" \
  --no-restore
```

Expected: **FAIL** — `ValidateIngredientStock` is `true` because `SubmitManufactureMapping.cs` explicitly sets it.

### Step 4.3: Apply the fix

- [ ] **Step 4.3: Remove `ValidateIngredientStock = true` from mapping**

In `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufacture/SubmitManufactureMapping.cs`, the current file is:

```csharp
using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.SubmitManufacture;

internal static class SubmitManufactureMapping
{
    public static SubmitManufactureClientRequest ToClientRequest(this SubmitManufactureRequest request)
    {
        return new SubmitManufactureClientRequest
        {
            ManufactureOrderCode = request.ManufactureOrderNumber,
            ManufactureInternalNumber = request.ManufactureInternalNumber,
            Date = request.Date,
            CreatedBy = request.CreatedBy,
            ManufactureType = request.ManufactureType,
            Items = request.Items.Select(item => new SubmitManufactureClientItem
            {
                ProductCode = item.ProductCode,
                Amount = item.Amount,
                ProductName = item.Name,
            }).ToList(),
            LotNumber = request.LotNumber,
            ExpirationDate = request.ExpirationDate,
            ValidateIngredientStock = true,
            ResidueDistribution = request.ResidueDistribution,
        };
    }
}
```

Replace with (remove `ValidateIngredientStock = true,`):

```csharp
using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.SubmitManufacture;

internal static class SubmitManufactureMapping
{
    public static SubmitManufactureClientRequest ToClientRequest(this SubmitManufactureRequest request)
    {
        return new SubmitManufactureClientRequest
        {
            ManufactureOrderCode = request.ManufactureOrderNumber,
            ManufactureInternalNumber = request.ManufactureInternalNumber,
            Date = request.Date,
            CreatedBy = request.CreatedBy,
            ManufactureType = request.ManufactureType,
            Items = request.Items.Select(item => new SubmitManufactureClientItem
            {
                ProductCode = item.ProductCode,
                Amount = item.Amount,
                ProductName = item.Name,
            }).ToList(),
            LotNumber = request.LotNumber,
            ExpirationDate = request.ExpirationDate,
            ResidueDistribution = request.ResidueDistribution,
        };
    }
}
```

### Step 4.4: Verify the fix

- [ ] **Step 4.4: Run the new test**

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~SubmitManufactureHandlerTests.Handle_DoesNotActivateIngredientStockValidation" \
  --no-restore
```

Expected: **PASS**.

### Step 4.5: Run the full handler test class

- [ ] **Step 4.5: Full class passes**

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~SubmitManufactureHandlerTests" \
  --no-restore
```

Expected: all existing tests + 1 new = all PASS.

### Step 4.6: Commit

- [ ] **Step 4.6: Commit**

```bash
git add \
  backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufacture/SubmitManufactureMapping.cs \
  backend/test/Anela.Heblo.Tests/Features/Manufacture/SubmitManufactureHandlerTests.cs

git commit -m "$(cat <<'EOF'
fix(manufacture): revert undocumented ValidateIngredientStock activation

The refactor silently flipped SubmitManufactureClientRequest.ValidateIngredientStock
from its default of false to true, activating client-side ingredient
stock validation on every submit. This was not in the old code and not
disclosed in PR #527. Combined with the rounding fix, strict less-than
comparisons can reject orders for sub-epsilon drift. If this feature is
wanted, it should land in a separate PR with documentation, regression
tests, and epsilon tolerance on the validator.
EOF
)"
```

---

## Task 5: Truncate `Note` to 2000-char column limit

**Why:** `ManufactureOrderNote.Text` has `HasMaxLength(2000)`. The combined note on product completion can include `"BoM update failures: {code}: {error}; ..."` which on large orders with long error strings exceeds 2000 chars. When the cap is exceeded, `SaveChangesAsync` throws `DbUpdateException` **after** Flexi has already applied partial updates — cross-system inconsistency with no rollback path.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/ConfirmProductCompletionWorkflow.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/Workflows/ConfirmProductCompletionWorkflowTests.cs`

### Step 5.1: Write the failing test

- [ ] **Step 5.1: Add note truncation test**

First, add the `CreateOrderWithManyProducts` helper to `ConfirmProductCompletionWorkflowTests` inside `#region Helper Methods`, next to `CreateSuccessfulUpdateOrderResponse`:

```csharp
private static UpdateManufactureOrderResponse CreateSuccessfulUpdateOrderResponseWithManyProducts(int productCount)
{
    var products = Enumerable.Range(1, productCount)
        .Select(i => new UpdateManufactureOrderProductDto
        {
            Id = i,
            ProductCode = $"P{i:D3}",
            ProductName = $"Product {i}",
            ActualQuantity = 1.0m,
            PlannedQuantity = 1.0m,
        })
        .ToList();

    return new UpdateManufactureOrderResponse
    {
        Success = true,
        Order = new UpdateManufactureOrderDto
        {
            OrderNumber = "MO-2024-LARGE",
            SemiProduct = new UpdateManufactureOrderSemiProductDto
            {
                ProductCode = "SP001001",
                ProductName = "Semi Product 1",
                ActualQuantity = 10.5m,
                PlannedQuantity = 10.5m,
                LotNumber = "LOT123",
                ExpirationDate = DateOnly.FromDateTime(DateTime.Today.AddDays(30)),
            },
            Products = products,
        },
    };
}
```

Then append the test before `#region Helper Methods`:

```csharp
[Fact]
public async Task ExecuteAsync_WhenBoMFailuresProduceOversizedNote_TruncatesToFit2000CharLimit()
{
    // Arrange — 30 products, each with a 200-char error message.
    // Raw note would be >> 2000 chars; it must be truncated to exactly 2000.
    const int ProductCount = 30;
    var updateOrderResponse = CreateSuccessfulUpdateOrderResponseWithManyProducts(ProductCount);
    var productQuantities = Enumerable.Range(1, ProductCount)
        .ToDictionary(i => i, _ => 1m);

    _mediatorMock
        .Setup(x => x.Send(It.IsAny<UpdateManufactureOrderRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(updateOrderResponse);

    _residueCalculatorMock
        .Setup(x => x.CalculateAsync(It.IsAny<UpdateManufactureOrderDto>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(CreateDistributionWithinThreshold());

    _mediatorMock
        .Setup(x => x.Send(It.IsAny<SubmitManufactureRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new SubmitManufactureResponse { Success = true, ManufactureId = "MFG-BIG-001" });

    // Every BoM update fails with a 200-char error string
    var longError = new string('Á', 200);
    _mediatorMock
        .Setup(x => x.Send(It.IsAny<UpdateBoMIngredientAmountRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new UpdateBoMIngredientAmountResponse
        {
            Success = false,
            UserMessage = longError,
        });

    UpdateManufactureOrderStatusRequest? capturedStatusRequest = null;
    _mediatorMock
        .Setup(x => x.Send(It.IsAny<UpdateManufactureOrderStatusRequest>(), It.IsAny<CancellationToken>()))
        .Callback<IRequest<UpdateManufactureOrderStatusResponse>, CancellationToken>(
            (r, _) => capturedStatusRequest = (UpdateManufactureOrderStatusRequest)r)
        .ReturnsAsync(new UpdateManufactureOrderStatusResponse { Success = true });

    // Act
    var result = await _workflow.ExecuteAsync(
        ValidOrderId, productQuantities, overrideConfirmed: false, changeReason: null, CancellationToken.None);

    // Assert
    capturedStatusRequest.Should().NotBeNull();
    capturedStatusRequest!.Note.Should().NotBeNull();
    capturedStatusRequest.Note!.Length.Should().BeLessThanOrEqualTo(2000);
    capturedStatusRequest.ManualActionRequired.Should().BeTrue();
}
```

### Step 5.2: Run the test and verify it fails

- [ ] **Step 5.2: Confirm failure**

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ConfirmProductCompletionWorkflowTests.ExecuteAsync_WhenBoMFailuresProduceOversizedNote_TruncatesToFit2000CharLimit" \
  --no-restore
```

Expected: **FAIL** — note length is well above 2000 chars.

### Step 5.3: Apply the fix

- [ ] **Step 5.3: Add `TruncateNote` and call it in `TransitionToCompletedAsync`**

In `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/ConfirmProductCompletionWorkflow.cs`:

**Step A** — Add two constants just after the private field declarations at the top of the class:

```csharp
// ManufactureOrderNote.Text is constrained to HasMaxLength(2000) — see
// ManufactureOrderNoteConfiguration.cs. Leave a margin for the suffix.
private const int MaxNoteLength = 2000;
private const string NoteTruncationSuffix = "… [truncated]";
```

**Step B** — Inside `TransitionToCompletedAsync`, add the truncation call just after the default-note fallback. The code currently is:

```csharp
if (string.IsNullOrEmpty(combined))
{
    combined = string.Format(
        ManufactureMessages.ProductCompletionDefaultNoteFormat,
        submitResult.ManufactureId);
}

var manualActionRequired = !submitResult.Success || bomFailures.Count > 0;
```

Change it to:

```csharp
if (string.IsNullOrEmpty(combined))
{
    combined = string.Format(
        ManufactureMessages.ProductCompletionDefaultNoteFormat,
        submitResult.ManufactureId);
}

combined = TruncateNote(combined);

var manualActionRequired = !submitResult.Success || bomFailures.Count > 0;
```

**Step C** — Add the private helper method at the bottom of the `ConfirmProductCompletionWorkflow` class:

```csharp
private static string TruncateNote(string note)
{
    if (note.Length <= MaxNoteLength)
    {
        return note;
    }

    var cutoff = MaxNoteLength - NoteTruncationSuffix.Length;
    return note.Substring(0, cutoff) + NoteTruncationSuffix;
}
```

### Step 5.4: Verify the fix

- [ ] **Step 5.4: Run the new test**

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ConfirmProductCompletionWorkflowTests.ExecuteAsync_WhenBoMFailuresProduceOversizedNote_TruncatesToFit2000CharLimit" \
  --no-restore
```

Expected: **PASS**.

### Step 5.5: Run full workflow test class

- [ ] **Step 5.5: Full class passes**

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ConfirmProductCompletionWorkflowTests" \
  --no-restore
```

Expected: all existing tests + 2 new (from Task 3 and this task) = all PASS.

### Step 5.6: Commit

- [ ] **Step 5.6: Commit**

```bash
git add \
  backend/src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/ConfirmProductCompletionWorkflow.cs \
  backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/Workflows/ConfirmProductCompletionWorkflowTests.cs

git commit -m "$(cat <<'EOF'
fix(manufacture): truncate product-completion Note to 2000-char DB column

ManufactureOrderNote.Text has HasMaxLength(2000). When BoM updates fail
for many products with long error messages, the combined note can exceed
2000 chars, causing UpdateManufactureOrderStatusHandler to throw
DbUpdateException after Flexi has already applied partial BoM updates —
cross-system inconsistency with no rollback. Defensively truncate the
combined note with a visible suffix and add a regression test using 30
products with long error strings.
EOF
)"
```

---

## Task 6: Final verification

**Files:** none modified — only verification commands.

### Step 6.1: Run the full backend test suite

- [ ] **Step 6.1: Full suite**

```bash
cd backend
dotnet test Anela.Heblo.sln --no-restore
```

Expected: all tests pass (existing baseline + ~6 new tests from this plan).

### Step 6.2: Verify code formatting

- [ ] **Step 6.2: Format check**

```bash
cd backend
dotnet format --verify-no-changes --no-restore
```

Expected: zero output, exit code 0. If issues are found, run `dotnet format --no-restore` and commit as a separate `style(manufacture): apply dotnet format` commit.

### Step 6.3: Build with warnings-as-errors

- [ ] **Step 6.3: Build check**

```bash
cd backend
dotnet build Anela.Heblo.sln --no-restore /warnaserror
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

### Step 6.4: Push the branch

- [ ] **Step 6.4: Push to `refactor/manufacture-pipeline`**

```bash
git push origin refactor/manufacture-pipeline
```

> **Note:** Do NOT push to `main`. This branch is the target of PR #527.

### Step 6.5: Add comment to PR #527

- [ ] **Step 6.5: Comment on PR #527 summarising fixes**

```bash
gh pr comment 527 --repo onpaj/Anela.Heblo --body "$(cat <<'EOF'
Addressed the four blocking findings from the code review (issue #577):

- **B1** — Restored `Math.Round(amount, 4)` in both consumption paths in `FlexiManufactureDocumentService` (reverts the merge-conflict regression of #572). Added regression tests in both `SubmitConsolidatedConsumptionAsync` and `SubmitConsumptionAsync`.
- **B2** — Re-populated `FlexiDocMaterialIssueForSemiProduct` and `FlexiDocSemiProductReceipt` in `ConfirmSemiProductManufactureWorkflow.UpdateStatusAsync`. Added regression test.
- **B3** — Re-populated `FlexiDocSemiProductIssueForProduct`, `FlexiDocMaterialIssueForProduct`, and `FlexiDocProductReceipt` in `ConfirmProductCompletionWorkflow.TransitionToCompletedAsync`. Added regression test.
- **B4** — Removed undocumented `ValidateIngredientStock = true` from `SubmitManufactureMapping`. If client-side ingredient stock validation is wanted, it should land in a dedicated PR with epsilon tolerance and documentation. Added regression test asserting the DTO default of `false` is preserved.
- **H1** — Truncated the combined `Note` in `ConfirmProductCompletionWorkflow` to the 2000-char `ManufactureOrderNote.Text` column limit with a `… [truncated]` suffix. Added regression test using 30 products with 200-char error strings.

Full backend suite passes (`dotnet test`), formatting is clean (`dotnet format --verify-no-changes`), warnings-as-errors build succeeds.
EOF
)"
```

---

## Self-review Checklist

Run these grep checks before declaring the plan complete:

| Check | Expected |
|---|---|
| `grep -c "var amount = Math.Round" backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/FlexiManufactureDocumentService.cs` | 2 |
| `grep -c "FlexiDocMaterialIssueForSemiProduct\|FlexiDocSemiProductReceipt" backend/src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/ConfirmSemiProductManufactureWorkflow.cs` | 2 |
| `grep -c "FlexiDocSemiProductIssueForProduct\|FlexiDocMaterialIssueForProduct\|FlexiDocProductReceipt" backend/src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/ConfirmProductCompletionWorkflow.cs` | 3 (at least) |
| `grep -c "ValidateIngredientStock" backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufacture/SubmitManufactureMapping.cs` | 0 |
| `grep -c "MaxNoteLength\|TruncateNote" backend/src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/ConfirmProductCompletionWorkflow.cs` | ≥ 3 |
| `dotnet test Anela.Heblo.sln --no-restore` | all pass |
| `dotnet format --verify-no-changes --no-restore` | clean |
