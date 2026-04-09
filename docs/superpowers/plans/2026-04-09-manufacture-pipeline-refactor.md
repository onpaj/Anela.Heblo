# Manufacture Submission Pipeline Refactor — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refactor `SubmitManufactureHandler`, `ManufactureOrderApplicationService`, and `FlexiManufactureClient` to remove dead code, decompose oversized methods, split adapter responsibilities, and surface silently-swallowed failures — without changing the `IManufactureClient` interface surface.

**Architecture:** Keep public contracts (`IManufactureClient`, `IManufactureOrderApplicationService`) stable. Internally, split `FlexiManufactureClient` (781 LOC) into 6 focused collaborators, route BoM updates through a new MediatR command, extract the two `Confirm*` multi-step workflows into dedicated orchestrator classes, and replace an 11-parameter helper with a record. Delete the legacy aggregated submission path (~150 LOC duplicated with the consolidated path). Add test coverage for BoM-update failures, name-truncation edge cases, and the currently-commented-out consumption/production failure tests.

**Tech Stack:** .NET 8, xUnit, FluentAssertions, Moq, MediatR, Clean Architecture with Vertical Slice organization. Follow `CLAUDE.md` rules — Allman braces, 4-space indent, classes (not records) for API DTOs, `dotnet format` must pass.

---

## Prerequisites

**Working directory:** This plan assumes you are working in a dedicated git worktree created off `main`. If not, create one first:

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
git worktree add ../Anela.Heblo-refactor-manufacture -b refactor/manufacture-pipeline main
cd ../Anela.Heblo-refactor-manufacture
```

**Baseline verification — run before starting:**

```bash
cd backend
dotnet build
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Manufacture"
dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj --filter "FullyQualifiedName~Manufacture"
```

Expected: build succeeds, all tests pass. If any baseline test fails, STOP and fix before continuing.

---

## File Structure

### Files to CREATE

**Application layer — Manufacture feature:**

| Path | Responsibility |
|---|---|
| `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureMessages.cs` | `static class` with Czech user-message constants (extracted from service + adapter) |
| `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufacture/SubmitManufactureMapping.cs` | `internal static` extension `ToClientRequest(this SubmitManufactureRequest)` |
| `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateBoMIngredientAmount/UpdateBoMIngredientAmountRequest.cs` | New MediatR request — `IRequest<UpdateBoMIngredientAmountResponse>` |
| `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateBoMIngredientAmount/UpdateBoMIngredientAmountResponse.cs` | Response class inheriting `BaseResponse` |
| `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateBoMIngredientAmount/UpdateBoMIngredientAmountHandler.cs` | Thin handler wrapping `IManufactureClient.UpdateBoMIngredientAmountAsync` |
| `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/UpdateOrderStatusCommand.cs` | Internal record replacing the 11-arg helper parameter list |
| `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/ConfirmSemiProductManufactureWorkflow.cs` | `IConfirmSemiProductManufactureWorkflow` + implementation |
| `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/ConfirmProductCompletionWorkflow.cs` | `IConfirmProductCompletionWorkflow` + implementation |
| `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/ManufactureNameBuilder.cs` | Extracts `CreateManufactureName` with safe-length semantics |

**Adapter layer — Flexi manufacture:**

| Path | Responsibility |
|---|---|
| `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/FlexiWarehouseResolver.cs` | `static class` mapping `ProductType` → warehouse id |
| `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/FlexiIngredientRequirementAggregator.cs` | Moves `AggregateIngredientRequirementsAsync` out of the god class |
| `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/FlexiIngredientStockValidator.cs` | Moves `ValidateIngredientStockAsync` out of the god class |
| `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/FlexiLotLoader.cs` | Moves `LoadAvailableLotsAsync` out of the god class |
| `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/FefoConsumptionAllocator.cs` | Moves `AllocateConsumptionItemsUsingFefo` + `AllocationEpsilon` constant out of the god class |
| `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/FlexiManufactureMovementService.cs` | Consolidated consumption + production movement submission (formerly the "Consolidated*" methods) |
| `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/FlexiManufactureTemplateService.cs` | `GetManufactureTemplateAsync` + `ResolveProductType` |
| `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureException.cs` | Typed exception with `OperationKind` enum |

**New tests:**

| Path | Covers |
|---|---|
| `backend/test/Anela.Heblo.Tests/Features/Manufacture/UseCases/UpdateBoMIngredientAmountHandlerTests.cs` | BoM update handler happy + error path |
| `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/Workflows/ConfirmProductCompletionWorkflowTests.cs` | Workflow steps in isolation |
| `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/Workflows/ConfirmSemiProductManufactureWorkflowTests.cs` | Workflow steps in isolation |
| `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/Workflows/ManufactureNameBuilderTests.cs` | Short-code + truncation edge cases |
| `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/Internal/FefoConsumptionAllocatorTests.cs` | FEFO allocation edge cases |
| `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/Internal/FlexiIngredientStockValidatorTests.cs` | Multi-item insufficient stock aggregation |

### Files to MODIFY

| Path | Why |
|---|---|
| `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufacture/SubmitManufactureHandler.cs` | Tighten catch, fix log placeholder, extract mapping |
| `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ManufactureOrderApplicationService.cs` | Delegate to workflows, drop `IManufactureClient` dep |
| `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureModule.cs` | Register new workflow classes |
| `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureClient.cs` | Delete dead code + legacy path; orchestrate via new collaborators |
| `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs` | Register new adapter collaborators; drop `IIssuedOrdersClient` dep |
| `backend/test/Anela.Heblo.Tests/Features/Manufacture/SubmitManufactureHandlerTests.cs` | Add cancellation + mapping tests |
| `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/ManufactureOrderApplicationServiceTests.cs` | Rewrite against new workflows |
| `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/FlexiManufactureClientTests.cs` | Uncomment failure-path tests, adapt to typed exception |
| `backend/src/Anela.Heblo.API/Controllers/ManufactureOrderController.cs` | Remove redundant outer try/catch if global middleware handles it (verify first) |

---

# Phase 1 — Quick Wins and Dead Code

## Task 1: Delete unused `_ordersClient`, `_logger` gets its first real use, dead code

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureClient.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs`

- [ ] **Step 1.1: Verify `_ordersClient` is not referenced anywhere in the file**

Run:
```bash
cd backend
grep -n "_ordersClient" src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureClient.cs
```

Expected: only the field declaration and constructor assignment appear. If any other line appears, STOP and investigate.

- [ ] **Step 1.2: Remove `_ordersClient` field, constructor parameter, using statement**

In `FlexiManufactureClient.cs`:

Delete line that reads:
```csharp
using Rem.FlexiBeeSDK.Client.Clients.IssuedOrders;
```

Delete line:
```csharp
private readonly IIssuedOrdersClient _ordersClient;
```

Delete constructor parameter `IIssuedOrdersClient ordersClient` and the assignment line `_ordersClient = ordersClient ?? throw new ArgumentNullException(nameof(ordersClient));`.

- [ ] **Step 1.3: Delete dead `MapToFlexiItem` method and unused warehouse-code constants**

Delete the entire `private static StockItemsMovementUpsertRequestItemFlexiDto MapToFlexiItem(...)` method (currently around lines 732–749).

Delete the two constant declarations:
```csharp
private const string WarehouseCodeSemiProduct = "POLOTOVARY";
private const string WarehouseCodeProduct = "ZBOZI";
```

- [ ] **Step 1.4: Update DI registration to drop `IIssuedOrdersClient` dependency**

In `FlexiAdapterServiceCollectionExtensions.cs`, find the `FlexiManufactureClient` registration (around line 72). Confirm it is a plain `services.AddScoped<IManufactureClient, FlexiManufactureClient>();` — no manual parameter wiring needed, DI resolves the new constructor signature automatically. No edit required unless manual `new FlexiManufactureClient(...)` exists.

- [ ] **Step 1.5: Build and run adapter tests**

Run:
```bash
cd backend
dotnet build src/Adapters/Anela.Heblo.Adapters.Flexi/Anela.Heblo.Adapters.Flexi.csproj
dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj --filter "FullyQualifiedName~Manufacture"
```

Expected: build + all existing tests pass. If tests pass `IIssuedOrdersClient` to the constructor explicitly, update them to drop that argument.

- [ ] **Step 1.6: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureClient.cs \
        backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs \
        backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/FlexiManufactureClientTests.cs
git commit -m "refactor(manufacture): remove dead code from FlexiManufactureClient

- drop unused IIssuedOrdersClient dependency
- delete unused MapToFlexiItem helper
- remove unused warehouse-code constants"
```

---

## Task 2: Fix `CreateManufactureOrderInErp` always-logs-success bug

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ManufactureOrderApplicationService.cs`

- [ ] **Step 2.1: Read the method**

Open `ManufactureOrderApplicationService.cs` lines 253–311. Verify the current body matches:

```csharp
var submitManufactureResult = await _mediator.Send(submitManufactureRequest, cancellationToken);
if (!submitManufactureResult.Success)
{
    _logger.LogError("Failed to create manufacture for order {OrderId}: {ErrorCode}",
        orderId, submitManufactureResult.ErrorCode);
}

_logger.LogInformation("Successfully created manufacture {ManufactureId} for order {OrderId}",
    submitManufactureResult.ManufactureId, orderId);
return submitManufactureResult;
```

- [ ] **Step 2.2: Write a failing test**

Append to `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/ManufactureOrderApplicationServiceTests.cs`:

```csharp
[Fact]
public async Task CreateManufactureOrderInErp_WhenSubmitFails_DoesNotLogSuccess()
{
    // Arrange
    var sut = BuildSut(); // use existing test harness in this file
    MediatorMock
        .Setup(m => m.Send(It.IsAny<SubmitManufactureRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new SubmitManufactureResponse(ErrorCodes.Exception));

    ArrangeUpdateOrderSuccess(orderId: 42); // existing helper — assume present

    // Act
    await sut.ConfirmSemiProductManufactureAsync(42, 10m, null, CancellationToken.None);

    // Assert — success log must NOT be emitted when submit failed
    LoggerMock.Verify(
        l => l.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Successfully created manufacture")),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
        Times.Never);
}
```

If the existing test file uses a different test-harness style (no `BuildSut()`), adapt the arrange block to match neighbours — reuse whatever mocks and builders are already defined at the top of the file.

- [ ] **Step 2.3: Run the test to verify it fails**

Run:
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~CreateManufactureOrderInErp_WhenSubmitFails_DoesNotLogSuccess"
```

Expected: FAIL — the success log is emitted unconditionally.

- [ ] **Step 2.4: Apply the fix**

Replace the tail of `CreateManufactureOrderInErp` with an `if/else`:

```csharp
var submitManufactureResult = await _mediator.Send(submitManufactureRequest, cancellationToken);
if (!submitManufactureResult.Success)
{
    _logger.LogError("Failed to create manufacture for order {OrderId}: {ErrorCode}",
        orderId, submitManufactureResult.ErrorCode);
}
else
{
    _logger.LogInformation("Successfully created manufacture {ManufactureId} for order {OrderId}",
        submitManufactureResult.ManufactureId, orderId);
}
return submitManufactureResult;
```

- [ ] **Step 2.5: Run the test and full suite**

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Manufacture"
```

Expected: all Manufacture tests pass including the new one.

- [ ] **Step 2.6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ManufactureOrderApplicationService.cs \
        backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/ManufactureOrderApplicationServiceTests.cs
git commit -m "fix(manufacture): don't log success after ERP submit failure"
```

---

## Task 3: Safe substring helpers for `CreateManufactureName`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ManufactureOrderApplicationService.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/Workflows/ManufactureNameBuilderTests.cs` (will be re-homed in Task 14 when we extract to `ManufactureNameBuilder`; for this task, add the tests in the existing service test file temporarily)

- [ ] **Step 3.1: Write three failing tests for edge cases**

In `ManufactureOrderApplicationServiceTests.cs` (temporary location):

```csharp
[Fact]
public async Task ConfirmProductCompletion_WhenProductCodeShorterThanPrefix_DoesNotThrow()
{
    var sut = BuildSut();
    var order = BuildOrderDto(semiProductCode: "ABC"); // 3 chars, less than prefix (6)
    ArrangeUpdateProductsSuccess(order);
    ArrangeSubmitManufactureSuccess();
    ArrangeUpdateStatusSuccess();

    var act = async () => await sut.ConfirmProductCompletionAsync(
        order.Id, new Dictionary<int, decimal> { { 1, 10m } }, overrideConfirmed: true, null, CancellationToken.None);

    await act.Should().NotThrowAsync();
}

[Fact]
public async Task ConfirmProductCompletion_WhenManufactureNameWouldExceed40Chars_IsTruncated()
{
    var sut = BuildSut();
    var order = BuildOrderDto(
        semiProductCode: "LONGCODE",
        semiProductName: "An extremely long semi-product name that would blow past 40 characters");
    ArrangeUpdateProductsSuccess(order);

    string? capturedInternalNumber = null;
    MediatorMock
        .Setup(m => m.Send(It.IsAny<SubmitManufactureRequest>(), It.IsAny<CancellationToken>()))
        .Callback<IRequest<SubmitManufactureResponse>, CancellationToken>((req, _) =>
            capturedInternalNumber = ((SubmitManufactureRequest)req).ManufactureInternalNumber)
        .ReturnsAsync(new SubmitManufactureResponse { ManufactureId = "M-1" });
    ArrangeUpdateStatusSuccess();

    await sut.ConfirmProductCompletionAsync(
        order.Id, new Dictionary<int, decimal> { { 1, 10m } }, overrideConfirmed: true, null, CancellationToken.None);

    capturedInternalNumber.Should().NotBeNull();
    capturedInternalNumber!.Length.Should().BeLessThanOrEqualTo(40);
}

[Fact]
public async Task ConfirmSemiProductManufacture_WhenProductCodeShorterThanPrefix_DoesNotThrow()
{
    var sut = BuildSut();
    ArrangeUpdateSemiProductSuccess(semiProductCode: "XY"); // 2 chars
    ArrangeSubmitManufactureSuccess();
    ArrangeUpdateStatusSuccess();

    var act = async () => await sut.ConfirmSemiProductManufactureAsync(1, 5m, null, CancellationToken.None);

    await act.Should().NotThrowAsync();
}
```

Adapt the `BuildOrderDto` / `ArrangeUpdate*` helpers to the existing test fixture shape. If no helpers exist, write inline Moq setups consistent with existing tests in the file.

- [ ] **Step 3.2: Run tests to verify first two fail (third may pass depending on state)**

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ConfirmProductCompletion_WhenProductCodeShorterThanPrefix_DoesNotThrow|FullyQualifiedName~ConfirmProductCompletion_WhenManufactureNameWouldExceed40Chars_IsTruncated"
```

Expected: first test FAILS with `ArgumentOutOfRangeException` from `Substring(0, 6)`.

- [ ] **Step 3.3: Add safe-substring helpers at the bottom of `ManufactureOrderApplicationService.cs`**

Add these named constants at the top of the class (just after the field declarations):

```csharp
private const int ProductCodePrefixLength = 6;
private const int MaxManufactureNameLength = 40;
```

Replace `CreateManufactureName` with:

```csharp
private string CreateManufactureName(UpdateManufactureOrderDto order, ErpManufactureType type)
{
    string manufactureName;
    var semiCode = order.SemiProduct.ProductCode;
    var shortName = _productNameFormatter.ShortProductName(order.SemiProduct.ProductName);
    var prefix = SafeTake(semiCode, ProductCodePrefixLength);

    if (type == ErpManufactureType.Product)
    {
        if (order.Products.All(p => p.ProductCode == semiCode)) // Singlephase manufacture
        {
            manufactureName = semiCode;
        }
        else
        {
            manufactureName = $"{prefix} {shortName}";
        }
    }
    else
    {
        manufactureName = $"{prefix}M {shortName}";
    }

    return SafeTake(manufactureName, MaxManufactureNameLength);
}

private static string SafeTake(string value, int maxLength)
{
    if (string.IsNullOrEmpty(value))
        return string.Empty;
    return value.Length <= maxLength ? value : value.Substring(0, maxLength);
}
```

- [ ] **Step 3.4: Run tests**

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Manufacture"
```

Expected: all pass.

- [ ] **Step 3.5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ManufactureOrderApplicationService.cs \
        backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/ManufactureOrderApplicationServiceTests.cs
git commit -m "fix(manufacture): safe substring in CreateManufactureName

Avoid ArgumentOutOfRangeException when product codes are shorter than
the 6-char prefix, and guarantee the internal manufacture number never
exceeds Flexi's 40-char limit."
```

---

# Phase 2 — SubmitManufactureHandler Hardening

## Task 4: Extract request mapping and fix log placeholder

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufacture/SubmitManufactureMapping.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufacture/SubmitManufactureHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Manufacture/SubmitManufactureHandlerTests.cs`

- [ ] **Step 4.1: Write a failing mapping round-trip test**

Append to `SubmitManufactureHandlerTests.cs`:

```csharp
[Fact]
public async Task Handle_MapsAllFieldsToClientRequest()
{
    SubmitManufactureClientRequest? captured = null;
    _clientMock
        .Setup(c => c.SubmitManufactureAsync(It.IsAny<SubmitManufactureClientRequest>(), It.IsAny<CancellationToken>()))
        .Callback<SubmitManufactureClientRequest, CancellationToken>((req, _) => captured = req)
        .ReturnsAsync("MAN-001");

    var request = new SubmitManufactureRequest
    {
        ManufactureOrderNumber = "MO-99",
        ManufactureInternalNumber = "INT-99",
        ManufactureType = ErpManufactureType.Product,
        Date = new DateTime(2026, 4, 9, 10, 0, 0, DateTimeKind.Utc),
        CreatedBy = "alice@anela.cz",
        Items = new List<SubmitManufactureRequestItem>
        {
            new() { ProductCode = "P-1", Name = "Product One", Amount = 12.5m },
            new() { ProductCode = "P-2", Name = "Product Two", Amount = 3.0m },
        },
        LotNumber = "LOT-A",
        ExpirationDate = new DateOnly(2027, 12, 31),
        ResidueDistribution = null,
    };

    await _handler.Handle(request, CancellationToken.None);

    captured.Should().NotBeNull();
    captured!.ManufactureOrderCode.Should().Be("MO-99");
    captured.ManufactureInternalNumber.Should().Be("INT-99");
    captured.ManufactureType.Should().Be(ErpManufactureType.Product);
    captured.Date.Should().Be(new DateTime(2026, 4, 9, 10, 0, 0, DateTimeKind.Utc));
    captured.CreatedBy.Should().Be("alice@anela.cz");
    captured.LotNumber.Should().Be("LOT-A");
    captured.ExpirationDate.Should().Be(new DateOnly(2027, 12, 31));
    captured.Items.Should().HaveCount(2);
    captured.Items[0].ProductCode.Should().Be("P-1");
    captured.Items[0].ProductName.Should().Be("Product One");
    captured.Items[0].Amount.Should().Be(12.5m);
}
```

- [ ] **Step 4.2: Run it**

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Handle_MapsAllFieldsToClientRequest"
```

Expected: PASS (mapping already exists, just unverified). This becomes a regression guardrail for the extraction in step 4.3.

- [ ] **Step 4.3: Create the mapping extension**

Create `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufacture/SubmitManufactureMapping.cs`:

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

- [ ] **Step 4.4: Replace inline mapping in `SubmitManufactureHandler.cs`**

Rewrite the handler body:

```csharp
public async Task<SubmitManufactureResponse> Handle(
    SubmitManufactureRequest request,
    CancellationToken cancellationToken)
{
    try
    {
        var manufactureId = await _manufactureClient.SubmitManufactureAsync(
            request.ToClientRequest(), cancellationToken);

        _logger.LogInformation("Successfully created manufacture {ManufactureId} for order {ManufactureOrderNumber}",
            manufactureId, request.ManufactureOrderNumber);

        return new SubmitManufactureResponse
        {
            ManufactureId = manufactureId
        };
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        _logger.LogError(ex, "Error creating manufacture for order {ManufactureOrderNumber}", request.ManufactureOrderNumber);
        return new SubmitManufactureResponse(ex)
        {
            UserMessage = _errorTransformer.Transform(ex)
        };
    }
}
```

Two functional changes: (1) `{ManufactureOrderId}` placeholder renamed to `{ManufactureOrderNumber}`, (2) `when (ex is not OperationCanceledException)` filter added.

- [ ] **Step 4.5: Write a failing cancellation-propagation test**

Append to `SubmitManufactureHandlerTests.cs`:

```csharp
[Fact]
public async Task Handle_WhenCancelled_PropagatesOperationCanceledException()
{
    _clientMock
        .Setup(c => c.SubmitManufactureAsync(It.IsAny<SubmitManufactureClientRequest>(), It.IsAny<CancellationToken>()))
        .ThrowsAsync(new OperationCanceledException());

    var act = async () => await _handler.Handle(BuildRequest(), new CancellationTokenSource().Token);

    await act.Should().ThrowAsync<OperationCanceledException>();
    _transformerMock.Verify(t => t.Transform(It.IsAny<Exception>()), Times.Never);
}
```

- [ ] **Step 4.6: Run tests**

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SubmitManufactureHandler"
```

Expected: all 5 tests pass (3 original + 2 new).

- [ ] **Step 4.7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufacture/ \
        backend/test/Anela.Heblo.Tests/Features/Manufacture/SubmitManufactureHandlerTests.cs
git commit -m "refactor(manufacture): harden SubmitManufactureHandler

- extract inline mapping into SubmitManufactureMapping extension
- propagate OperationCanceledException instead of swallowing it
- fix mis-named {ManufactureOrderId} log placeholder"
```

---

# Phase 3 — FlexiManufactureClient: Delete Legacy Path

## Task 5: Route all submissions through the consolidated path

**Context:** `SubmitManufactureAggregatedAsync` + `SubmitConsumptionMovementsAsync` + `SubmitProductionMovementAsync` (lines ~94–478) duplicate ~85% of the consolidated versions. The semi-product flow can safely use the consolidated methods because `request.Items` contains the single semi-product line and the consolidated helpers handle any item count.

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureClient.cs`
- Modify: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/FlexiManufactureClientTests.cs`

- [ ] **Step 5.1: Identify legacy-only test assertions**

Run:
```bash
cd backend
grep -n "SubmitManufactureAggregatedAsync\|SubmitConsumptionMovementsAsync\|SubmitProductionMovementAsync\|totalConsumptionCost" \
  test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/FlexiManufactureClientTests.cs
```

Note any tests that assert on the legacy code path explicitly (there should be none — all are via the public `SubmitManufactureAsync`). If any test asserts a `double` return value from a deleted private method via reflection, rewrite it to assert via public API calls instead.

- [ ] **Step 5.2: Rewrite `SubmitManufactureAsync` to always use the per-product flow**

In `FlexiManufactureClient.cs`, replace:

```csharp
public async Task<string> SubmitManufactureAsync(SubmitManufactureClientRequest request, CancellationToken cancellationToken = default)
{
    if (request.ManufactureType == ErpManufactureType.Product)
    {
        await SubmitManufacturePerProductAsync(request, cancellationToken);
    }
    else
    {
        await SubmitManufactureAggregatedAsync(request, cancellationToken);
    }
    return request.ManufactureOrderCode;
}
```

with:

```csharp
public async Task<string> SubmitManufactureAsync(SubmitManufactureClientRequest request, CancellationToken cancellationToken = default)
{
    await SubmitManufacturePerProductAsync(request, cancellationToken);
    return request.ManufactureOrderCode;
}
```

- [ ] **Step 5.3: Delete the three legacy private methods**

Delete:
- `private async Task SubmitManufactureAggregatedAsync(...)` (lines ~94–105)
- `private async Task<double> SubmitConsumptionMovementsAsync(...)` (lines ~354–424)
- `private async Task SubmitProductionMovementAsync(...)` (lines ~429–478)

- [ ] **Step 5.4: Run the adapter test suite**

```bash
cd backend
dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj \
  --filter "FullyQualifiedName~Manufacture"
```

Expected: all existing tests pass. The SemiProduct-type tests now exercise the consolidated path. If any test fails because it mocks `StockToDateAsync` with warehouse-specific setups that no longer match, widen the setups — the consolidated path calls the same underlying client methods.

- [ ] **Step 5.5: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureClient.cs \
        backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/FlexiManufactureClientTests.cs
git commit -m "refactor(manufacture): delete legacy aggregated submission path

Both SemiProduct and Product flows now go through the consolidated
per-product path. Removes ~150 LOC of near-duplicate code."
```

---

## Task 6: Extract `FlexiWarehouseResolver`

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/FlexiWarehouseResolver.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureClient.cs`

- [ ] **Step 6.1: Create the helper**

Create `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/FlexiWarehouseResolver.cs`:

```csharp
using Anela.Heblo.Adapters.Flexi.Stock;
using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Adapters.Flexi.Manufacture.Internal;

internal static class FlexiWarehouseResolver
{
    public static int ForProductType(ProductType type) => type switch
    {
        ProductType.Material => FlexiStockClient.MaterialWarehouseId,
        ProductType.SemiProduct => FlexiStockClient.SemiProductsWarehouseId,
        ProductType.Product or ProductType.Goods => FlexiStockClient.ProductsWarehouseId,
        _ => FlexiStockClient.MaterialWarehouseId
    };
}
```

- [ ] **Step 6.2: Replace inline switches in `FlexiManufactureClient.cs`**

Replace all `.GroupBy(item => item.ProductType switch { ... })` occurrences (currently in `ValidateIngredientStockAsync` ~line 233 and `SubmitConsolidatedConsumptionMovementsAsync` ~line 489) with `.GroupBy(item => FlexiWarehouseResolver.ForProductType(item.ProductType))`.

Add `using Anela.Heblo.Adapters.Flexi.Manufacture.Internal;` at the top of `FlexiManufactureClient.cs`.

- [ ] **Step 6.3: Run tests**

```bash
cd backend
dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj \
  --filter "FullyQualifiedName~Manufacture"
```

Expected: all pass.

- [ ] **Step 6.4: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/
git commit -m "refactor(manufacture): extract FlexiWarehouseResolver

Remove duplicate ProductType→warehouse switch from FlexiManufactureClient."
```

---

## Task 7: Introduce `FlexiManufactureException`

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureException.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureClient.cs`

- [ ] **Step 7.1: Create the exception type**

Create `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureException.cs`:

```csharp
namespace Anela.Heblo.Adapters.Flexi.Manufacture;

public enum FlexiManufactureOperationKind
{
    StockValidation,
    TemplateFetch,
    LotLoading,
    ConsumptionMovement,
    ProductionMovement,
    BoMUpdate,
    Allocation
}

public class FlexiManufactureException : Exception
{
    public FlexiManufactureOperationKind OperationKind { get; }
    public int? WarehouseId { get; }
    public string? RawFlexiError { get; }

    public FlexiManufactureException(
        FlexiManufactureOperationKind operationKind,
        string message,
        int? warehouseId = null,
        string? rawFlexiError = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        OperationKind = operationKind;
        WarehouseId = warehouseId;
        RawFlexiError = rawFlexiError;
    }
}
```

- [ ] **Step 7.2: Replace all raw `InvalidOperationException` throws in `FlexiManufactureClient.cs`**

Search for the throws:
```bash
cd backend
grep -n "throw new InvalidOperationException" \
  src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureClient.cs
```

Replace each:

1. `ValidateIngredientStockAsync` (formerly ~line 253):
```csharp
throw new FlexiManufactureException(
    FlexiManufactureOperationKind.StockValidation,
    $"Insufficient stock for ingredients: {string.Join("; ", insufficientIngredients)}");
```

2. `AllocateConsumptionItemsUsingFefo` (formerly ~line 341):
```csharp
throw new FlexiManufactureException(
    FlexiManufactureOperationKind.Allocation,
    $"Cannot allocate full amount for ingredient {requirement.ProductCode}: {remainingToAllocate:F3} remaining");
```

3. `SubmitConsolidatedConsumptionMovementsAsync` (formerly ~line 544):
```csharp
throw new FlexiManufactureException(
    FlexiManufactureOperationKind.ConsumptionMovement,
    $"Failed to create consumption stock movement for warehouse {warehouseId}",
    warehouseId: warehouseId,
    rawFlexiError: consumptionResult.GetErrorMessage());
```

4. `SubmitConsolidatedProductionMovementAsync` (formerly ~line 600):
```csharp
throw new FlexiManufactureException(
    FlexiManufactureOperationKind.ProductionMovement,
    "Failed to create production stock movement",
    rawFlexiError: productionResult.GetErrorMessage());
```

- [ ] **Step 7.3: Check `IManufactureErrorTransformer` still handles these**

Read `backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/ManufactureErrorTransformer.cs` and its filter classes. If any filter matches on `InvalidOperationException` specifically, either (a) loosen the match to `Exception` + message sniffing, or (b) add a new filter that recognises `FlexiManufactureException` by `OperationKind`. The refactor's goal is to enable (b) later; for this step, the minimum change is making sure existing tests still pass. If a filter does substring matching on `ex.Message`, the message text above has been preserved.

- [ ] **Step 7.4: Uncomment and rewrite the previously-commented failure tests**

In `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/FlexiManufactureClientTests.cs` near line 561, locate the commented-out tests. Replace with:

```csharp
[Fact]
public async Task SubmitManufactureAsync_WhenConsumptionMovementFails_ThrowsFlexiManufactureException()
{
    var sut = BuildSut(); // use existing test builder
    ArrangeValidBoMAndStock();

    _stockMovementClientMock
        .Setup(c => c.SaveAsync(It.Is<StockItemsMovementUpsertRequestFlexiDto>(r => r.StockMovementDirection == StockMovementDirection.Out), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new SaveResult { IsSuccess = false, ErrorMessage = "Flexi stock out of range" });

    var act = async () => await sut.SubmitManufactureAsync(BuildProductRequest(), CancellationToken.None);

    var ex = await act.Should().ThrowAsync<FlexiManufactureException>();
    ex.Which.OperationKind.Should().Be(FlexiManufactureOperationKind.ConsumptionMovement);
    ex.Which.RawFlexiError.Should().Be("Flexi stock out of range");
}

[Fact]
public async Task SubmitManufactureAsync_WhenProductionMovementFails_ThrowsFlexiManufactureException()
{
    var sut = BuildSut();
    ArrangeValidBoMAndStock();

    _stockMovementClientMock
        .Setup(c => c.SaveAsync(It.Is<StockItemsMovementUpsertRequestFlexiDto>(r => r.StockMovementDirection == StockMovementDirection.Out), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new SaveResult { IsSuccess = true });
    _stockMovementClientMock
        .Setup(c => c.SaveAsync(It.Is<StockItemsMovementUpsertRequestFlexiDto>(r => r.StockMovementDirection == StockMovementDirection.In), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new SaveResult { IsSuccess = false, ErrorMessage = "Flexi production error" });

    var act = async () => await sut.SubmitManufactureAsync(BuildProductRequest(), CancellationToken.None);

    var ex = await act.Should().ThrowAsync<FlexiManufactureException>();
    ex.Which.OperationKind.Should().Be(FlexiManufactureOperationKind.ProductionMovement);
}
```

Adapt `BuildSut`, `ArrangeValidBoMAndStock`, `BuildProductRequest`, and `SaveResult`/`GetErrorMessage()` to match whatever helpers already exist in the test file. If no helper exists, replicate the mocks from the nearest neighbour test.

- [ ] **Step 7.5: Run tests**

```bash
cd backend
dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj \
  --filter "FullyQualifiedName~Manufacture"
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ManufactureErrorTransformer|FullyQualifiedName~SubmitManufactureHandler"
```

Expected: all pass.

- [ ] **Step 7.6: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/ \
        backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/FlexiManufactureClientTests.cs
git commit -m "refactor(manufacture): introduce typed FlexiManufactureException

Replace raw InvalidOperationException throws with typed exception
carrying OperationKind, WarehouseId, and RawFlexiError. Enables the
error transformer to route messages without message-sniffing.

Re-enables the previously-commented-out failure-path tests."
```

---

# Phase 4 — FlexiManufactureClient: Extract Collaborators

**For each extraction task below**, the pattern is identical: move a region of code from `FlexiManufactureClient.cs` into a new `Internal/Flexi*.cs` file, register the new class in DI, inject it back into `FlexiManufactureClient`, and delete the moved code. After each extraction, run the Flexi adapter test suite.

## Task 8: Extract `FlexiManufactureTemplateService`

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/IFlexiManufactureTemplateService.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/FlexiManufactureTemplateService.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureClient.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs`

- [ ] **Step 8.1: Define the interface**

Create `IFlexiManufactureTemplateService.cs`:

```csharp
using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Adapters.Flexi.Manufacture.Internal;

internal interface IFlexiManufactureTemplateService
{
    Task<ManufactureTemplate?> GetManufactureTemplateAsync(string productCode, CancellationToken cancellationToken = default);
}
```

(Confirm the exact return type is `ManufactureTemplate` by reading `IManufactureClient.cs`. If different, mirror the existing signature.)

- [ ] **Step 8.2: Implement the service**

Create `FlexiManufactureTemplateService.cs`:

```csharp
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Manufacture;
using Rem.FlexiBeeSDK.Client.Clients.Products.BoM;

namespace Anela.Heblo.Adapters.Flexi.Manufacture.Internal;

internal sealed class FlexiManufactureTemplateService : IFlexiManufactureTemplateService
{
    private readonly IBoMClient _bomClient;
    private readonly IErpStockClient _stockClient;
    private readonly TimeProvider _timeProvider;

    public FlexiManufactureTemplateService(IBoMClient bomClient, IErpStockClient stockClient, TimeProvider timeProvider)
    {
        _bomClient = bomClient;
        _stockClient = stockClient;
        _timeProvider = timeProvider;
    }

    public async Task<ManufactureTemplate?> GetManufactureTemplateAsync(string productCode, CancellationToken cancellationToken = default)
    {
        // PASTE the body of the current GetManufactureTemplateAsync from FlexiManufactureClient.cs
        // plus the ResolveProductType helper below, unchanged.
        throw new NotImplementedException("Copy body from FlexiManufactureClient.GetManufactureTemplateAsync lines 611-670");
    }

    private static ProductType ResolveProductType(string? flexiTypeCode)
    {
        // Copy body from FlexiManufactureClient.ResolveProductType lines 672-698
        throw new NotImplementedException();
    }
}
```

Then literally paste the bodies from the original `GetManufactureTemplateAsync` (~lines 611–670) and `ResolveProductType` (~lines 672–698), replacing the `throw new NotImplementedException(...)` stubs.

- [ ] **Step 8.3: Register in DI**

In `FlexiAdapterServiceCollectionExtensions.cs`, add near the `FlexiManufactureClient` registration:

```csharp
services.AddScoped<IFlexiManufactureTemplateService, FlexiManufactureTemplateService>();
```

- [ ] **Step 8.4: Use the service in `FlexiManufactureClient`**

Add to the client's constructor:

```csharp
private readonly IFlexiManufactureTemplateService _templateService;

public FlexiManufactureClient(
    /* existing params */
    IFlexiManufactureTemplateService templateService,
    /* existing params */)
{
    // ...
    _templateService = templateService;
}
```

Replace any internal call `GetManufactureTemplateAsync(...)` inside the class (there's one in `AggregateIngredientRequirementsAsync` ~line 188) with `_templateService.GetManufactureTemplateAsync(...)`.

Delete the `GetManufactureTemplateAsync` and `ResolveProductType` methods from `FlexiManufactureClient.cs`. Keep the **public** `IManufactureClient.GetManufactureTemplateAsync` method that delegates to `_templateService`:

```csharp
public Task<ManufactureTemplate?> GetManufactureTemplateAsync(string productCode, CancellationToken cancellationToken = default)
    => _templateService.GetManufactureTemplateAsync(productCode, cancellationToken);
```

- [ ] **Step 8.5: Drop now-unused `_bomClient`/`_stockClient` dependencies if possible**

Read the remaining usages of `_bomClient` and `_stockClient` in `FlexiManufactureClient.cs`:

```bash
grep -n "_bomClient\|_stockClient" \
  backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureClient.cs
```

- `_bomClient`: still used by `UpdateBoMIngredientAmountAsync`. Keep.
- `_stockClient`: may still be used inside the consolidated consumption method (which we haven't extracted yet). Keep for now; revisit after Task 10.

- [ ] **Step 8.6: Run tests**

```bash
cd backend
dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj \
  --filter "FullyQualifiedName~Manufacture"
```

Expected: all existing tests pass.

- [ ] **Step 8.7: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/
git commit -m "refactor(manufacture): extract FlexiManufactureTemplateService"
```

---

## Task 9: Extract `FefoConsumptionAllocator`, `FlexiIngredientRequirementAggregator`, `FlexiIngredientStockValidator`, `FlexiLotLoader`

These four extractions follow the same pattern as Task 8. Do them one at a time with a commit between each.

**Files per extraction:**

| New file | Extracted from `FlexiManufactureClient.cs` lines |
|---|---|
| `Internal/IFefoConsumptionAllocator.cs` + `FefoConsumptionAllocator.cs` | `AllocateConsumptionItemsUsingFefo` (~285–352) |
| `Internal/IFlexiIngredientRequirementAggregator.cs` + `FlexiIngredientRequirementAggregator.cs` | `AggregateIngredientRequirementsAsync` (~180–222) |
| `Internal/IFlexiIngredientStockValidator.cs` + `FlexiIngredientStockValidator.cs` | `ValidateIngredientStockAsync` (~224–259) |
| `Internal/IFlexiLotLoader.cs` + `FlexiLotLoader.cs` | `LoadAvailableLotsAsync` (~261–283) |

- [ ] **Step 9.1: Extract `FefoConsumptionAllocator` (stateless, easiest first)**

Create the interface:

```csharp
// backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/IFefoConsumptionAllocator.cs
namespace Anela.Heblo.Adapters.Flexi.Manufacture.Internal;

internal interface IFefoConsumptionAllocator
{
    List<ConsumptionItem> Allocate(
        Dictionary<string, IngredientRequirement> requirements,
        Dictionary<string, List<LotStock>> lots,
        string? sourceProductCode = null);
}
```

(`LotStock` is the existing type used in `LoadAvailableLotsAsync`. If the real type name differs, match it.)

Create `FefoConsumptionAllocator.cs`:

```csharp
using Anela.Heblo.Domain.Features.Catalog.Lots;

namespace Anela.Heblo.Adapters.Flexi.Manufacture.Internal;

internal sealed class FefoConsumptionAllocator : IFefoConsumptionAllocator
{
    public const double AllocationEpsilon = 0.001;

    public List<ConsumptionItem> Allocate(
        Dictionary<string, IngredientRequirement> requirements,
        Dictionary<string, List<LotStock>> lots,
        string? sourceProductCode = null)
    {
        // PASTE body of AllocateConsumptionItemsUsingFefo from FlexiManufactureClient.cs lines 285-352
        // replacing the `0.001` literal with AllocationEpsilon.
        throw new NotImplementedException();
    }
}
```

Paste the body literally, then replace the `> 0.001` literal on the original line 341 with `> AllocationEpsilon`.

**Note:** `IngredientRequirement` and `ConsumptionItem` are currently `internal sealed` classes at the top of `FlexiManufactureClient.cs` (lines 18–36). Move them into a new file `Internal/ManufactureWorkItems.cs` so both the allocator and the client can reference them without circular visibility issues. They stay `internal sealed`.

- [ ] **Step 9.2: Register, inject, delete original. Run tests. Commit.**

```bash
# Register in FlexiAdapterServiceCollectionExtensions.cs:
#   services.AddScoped<IFefoConsumptionAllocator, FefoConsumptionAllocator>();
# Inject into FlexiManufactureClient and replace the call at ~line 167/102.
# Delete the private AllocateConsumptionItemsUsingFefo from the client.

cd backend
dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj \
  --filter "FullyQualifiedName~Manufacture"

git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/
git commit -m "refactor(manufacture): extract FefoConsumptionAllocator"
```

- [ ] **Step 9.3: Write focused tests for the allocator**

Create `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/Internal/FefoConsumptionAllocatorTests.cs`:

```csharp
using Anela.Heblo.Adapters.Flexi.Manufacture.Internal;
using Anela.Heblo.Domain.Features.Catalog;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Manufacture.Internal;

public class FefoConsumptionAllocatorTests
{
    private readonly FefoConsumptionAllocator _sut = new();

    [Fact]
    public void Allocate_WhenSingleLotCoversRequirement_ReturnsSingleItem()
    {
        var requirements = new Dictionary<string, IngredientRequirement>
        {
            ["MAT-1"] = new()
            {
                ProductCode = "MAT-1",
                ProductName = "Material 1",
                ProductType = ProductType.Material,
                RequiredAmount = 100,
                HasLots = true
            }
        };
        var lots = new Dictionary<string, List<LotStock>>
        {
            ["MAT-1"] = new()
            {
                new LotStock { LotNumber = "LOT-A", Expiration = new DateOnly(2027, 1, 1), Amount = 150 }
            }
        };

        var result = _sut.Allocate(requirements, lots);

        result.Should().HaveCount(1);
        result[0].LotNumber.Should().Be("LOT-A");
        result[0].Amount.Should().Be(100);
    }

    [Fact]
    public void Allocate_WhenMultipleLotsNeeded_UsesEarliestExpirationFirst()
    {
        var requirements = new Dictionary<string, IngredientRequirement>
        {
            ["MAT-1"] = new()
            {
                ProductCode = "MAT-1",
                ProductName = "Material 1",
                ProductType = ProductType.Material,
                RequiredAmount = 120,
                HasLots = true
            }
        };
        var lots = new Dictionary<string, List<LotStock>>
        {
            ["MAT-1"] = new()
            {
                new LotStock { LotNumber = "LOT-LATE", Expiration = new DateOnly(2028, 1, 1), Amount = 100 },
                new LotStock { LotNumber = "LOT-EARLY", Expiration = new DateOnly(2026, 1, 1), Amount = 100 }
            }
        };

        var result = _sut.Allocate(requirements, lots);

        result.Should().HaveCount(2);
        result[0].LotNumber.Should().Be("LOT-EARLY");
        result[0].Amount.Should().Be(100);
        result[1].LotNumber.Should().Be("LOT-LATE");
        result[1].Amount.Should().Be(20);
    }

    [Fact]
    public void Allocate_WhenLotsInsufficient_ThrowsFlexiManufactureException()
    {
        var requirements = new Dictionary<string, IngredientRequirement>
        {
            ["MAT-1"] = new()
            {
                ProductCode = "MAT-1",
                ProductName = "Material 1",
                ProductType = ProductType.Material,
                RequiredAmount = 100,
                HasLots = true
            }
        };
        var lots = new Dictionary<string, List<LotStock>>
        {
            ["MAT-1"] = new() { new LotStock { LotNumber = "LOT-A", Expiration = new DateOnly(2027, 1, 1), Amount = 50 } }
        };

        var act = () => _sut.Allocate(requirements, lots);

        act.Should().Throw<FlexiManufactureException>()
           .Which.OperationKind.Should().Be(FlexiManufactureOperationKind.Allocation);
    }

    [Fact]
    public void Allocate_WhenRemainderWithinEpsilon_DoesNotThrow()
    {
        var requirements = new Dictionary<string, IngredientRequirement>
        {
            ["MAT-1"] = new()
            {
                ProductCode = "MAT-1",
                ProductName = "Material 1",
                ProductType = ProductType.Material,
                RequiredAmount = 100.0005, // within AllocationEpsilon of 100
                HasLots = true
            }
        };
        var lots = new Dictionary<string, List<LotStock>>
        {
            ["MAT-1"] = new() { new LotStock { LotNumber = "LOT-A", Expiration = new DateOnly(2027, 1, 1), Amount = 100 } }
        };

        var act = () => _sut.Allocate(requirements, lots);

        act.Should().NotThrow();
    }
}
```

Adapt `LotStock` field names to match the real type (read `backend/src/Anela.Heblo.Domain/Features/Catalog/Lots/LotStock.cs` or similar).

- [ ] **Step 9.4: Run the new allocator tests**

```bash
cd backend
dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj \
  --filter "FullyQualifiedName~FefoConsumptionAllocatorTests"
```

Expected: all 4 pass.

- [ ] **Step 9.5: Commit allocator tests**

```bash
git add backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/Internal/FefoConsumptionAllocatorTests.cs
git commit -m "test(manufacture): add FefoConsumptionAllocator unit tests"
```

- [ ] **Step 9.6: Repeat the extract-register-inject-delete-test-commit cycle for the remaining three**

Order: `FlexiIngredientRequirementAggregator` → `FlexiIngredientStockValidator` → `FlexiLotLoader`.

For each: interface + impl in `Internal/`, DI registration, inject into `FlexiManufactureClient`, replace inline call, delete original method, run full Manufacture test suite, commit with message `refactor(manufacture): extract <ClassName>`.

For `FlexiIngredientStockValidator`, also write a new test file:

```csharp
// backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/Internal/FlexiIngredientStockValidatorTests.cs

[Fact]
public async Task Validate_WhenMultipleIngredientsInsufficient_AggregatesAllIntoOneException()
{
    // Arrange 3 ingredients where 2 have insufficient stock
    // Act
    var act = async () => await _sut.ValidateAsync(requirements, CancellationToken.None);
    // Assert exception message contains both ingredient codes
    var ex = await act.Should().ThrowAsync<FlexiManufactureException>();
    ex.Which.OperationKind.Should().Be(FlexiManufactureOperationKind.StockValidation);
    ex.Which.Message.Should().Contain("MAT-1").And.Contain("MAT-2");
}
```

Commit each extraction separately. Four commits total in this step.

---

## Task 10: Extract `FlexiManufactureMovementService`

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/IFlexiManufactureMovementService.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/Internal/FlexiManufactureMovementService.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureClient.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs`

- [ ] **Step 10.1: Define interface**

Create `IFlexiManufactureMovementService.cs`:

```csharp
using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Adapters.Flexi.Manufacture.Internal;

internal interface IFlexiManufactureMovementService
{
    Task SubmitConsolidatedConsumptionAsync(
        SubmitManufactureClientRequest request,
        List<ConsumptionItem> consumptionItems,
        Dictionary<string, double> productCosts,
        CancellationToken cancellationToken);

    Task SubmitConsolidatedProductionAsync(
        SubmitManufactureClientRequest request,
        Dictionary<string, double> productCosts,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 10.2: Implement**

Create `FlexiManufactureMovementService.cs` with constructor taking `IErpStockClient` and `IStockItemsMovementClient`. Paste the bodies of `SubmitConsolidatedConsumptionMovementsAsync` and `SubmitConsolidatedProductionMovementAsync` (lines ~482–604 of the original `FlexiManufactureClient.cs`), adjusting field references (`_stockClient` and `_stockMovementClient` now belong to the new service).

- [ ] **Step 10.3: Register, inject, delete original, run tests, commit**

```csharp
// In FlexiAdapterServiceCollectionExtensions.cs:
services.AddScoped<IFlexiManufactureMovementService, FlexiManufactureMovementService>();
```

In `FlexiManufactureClient`, inject `IFlexiManufactureMovementService _movementService`. In `SubmitManufacturePerProductAsync`, replace the two `SubmitConsolidated*` calls with `_movementService.SubmitConsolidatedConsumptionAsync(...)` and `_movementService.SubmitConsolidatedProductionAsync(...)`.

Delete the two methods from the client. Drop `_stockClient` and `_stockMovementClient` fields if no other code in the client references them.

```bash
cd backend
dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj --filter "FullyQualifiedName~Manufacture"

git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/
git commit -m "refactor(manufacture): extract FlexiManufactureMovementService"
```

---

## Task 11: `FlexiManufactureClient` is now a thin facade — verify LOC target

- [ ] **Step 11.1: Measure**

```bash
cd backend
wc -l src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureClient.cs
```

Expected: ~200 LOC or less. Remaining content should be: field declarations, constructor, `SubmitManufactureAsync`, `SubmitManufacturePerProductAsync` (orchestrator), `UpdateBoMIngredientAmountAsync`, `FindByIngredientAsync`, `GetSetPartsAsync`, `GetManufactureTemplateAsync` delegator, `GetConsumptionDocumentType`, `GetProductionDocumentType`, `CreateDescription`.

If significantly larger, the orchestrator still contains moved logic — review and complete extraction.

- [ ] **Step 11.2: Final adapter-suite run**

```bash
cd backend
dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj
```

Expected: all tests pass.

---

# Phase 5 — ManufactureOrderApplicationService Decomposition

## Task 12: Introduce `UpdateOrderStatusCommand` record

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/UpdateOrderStatusCommand.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ManufactureOrderApplicationService.cs`

- [ ] **Step 12.1: Create the record**

```csharp
// backend/src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/UpdateOrderStatusCommand.cs
using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Application.Features.Manufacture.Services.Workflows;

internal sealed record ManufactureDocumentCodes(
    string? SemiProduct,
    string? Product,
    string? Discard);

internal sealed record WeightToleranceInfo(
    bool WithinTolerance,
    decimal Difference);

internal sealed record UpdateOrderStatusCommand(
    int OrderId,
    ManufactureOrderState TargetState,
    string ChangeReason,
    string Note,
    ManufactureDocumentCodes Documents,
    bool ManualActionRequired,
    WeightToleranceInfo? WeightTolerance);
```

- [ ] **Step 12.2: Refactor the `UpdateOrderStatus` helper signature**

In `ManufactureOrderApplicationService.cs`, replace the 11-parameter helper with:

```csharp
private async Task<UpdateManufactureOrderStatusResponse> UpdateOrderStatus(
    UpdateOrderStatusCommand command,
    CancellationToken cancellationToken)
{
    var statusRequest = new UpdateManufactureOrderStatusRequest
    {
        Id = command.OrderId,
        NewState = command.TargetState,
        ChangeReason = command.ChangeReason,
        Note = command.Note,
        SemiProductOrderCode = command.Documents.SemiProduct,
        ProductOrderCode = command.Documents.Product,
        DiscardRedisueDocumentCode = command.Documents.Discard,
        ManualActionRequired = command.ManualActionRequired,
        WeightWithinTolerance = command.WeightTolerance?.WithinTolerance,
        WeightDifference = command.WeightTolerance?.Difference,
    };

    return await _mediator.Send(statusRequest, cancellationToken);
}
```

Update both call sites (in `ConfirmSemiProductManufactureAsync` ~line 64 and `ConfirmProductCompletionAsync` ~line 171) to build an `UpdateOrderStatusCommand` and pass it. Example for the semi-product call:

```csharp
var result = await UpdateOrderStatus(
    new UpdateOrderStatusCommand(
        OrderId: orderId,
        TargetState: ManufactureOrderState.SemiProductManufactured,
        ChangeReason: changeReason ?? $"Potvrzeno skutečné množství polotovaru: {actualQuantity}",
        Note: submitManufactureResult.Success
            ? $"Vytvořena vydaná objednávka meziproduktu {submitManufactureResult.ManufactureId}"
            : submitManufactureResult.UserMessage ?? submitManufactureResult.FullError(),
        Documents: new ManufactureDocumentCodes(
            SemiProduct: submitManufactureResult.ManufactureId,
            Product: null,
            Discard: null),
        ManualActionRequired: !submitManufactureResult.Success,
        WeightTolerance: null),
    cancellationToken);
```

- [ ] **Step 12.3: Run tests**

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Manufacture"
```

Expected: all pass. The behavior is unchanged, only the internal helper signature.

- [ ] **Step 12.4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/Services/
git commit -m "refactor(manufacture): introduce UpdateOrderStatusCommand record

Replace 11-parameter UpdateOrderStatus helper with a typed command
record, grouping document codes and weight-tolerance info."
```

---

## Task 13: Route BoM update through MediatR

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateBoMIngredientAmount/UpdateBoMIngredientAmountRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateBoMIngredientAmount/UpdateBoMIngredientAmountResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateBoMIngredientAmount/UpdateBoMIngredientAmountHandler.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Manufacture/UseCases/UpdateBoMIngredientAmountHandlerTests.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ManufactureOrderApplicationService.cs`

- [ ] **Step 13.1: Create request**

```csharp
// UpdateBoMIngredientAmountRequest.cs
using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateBoMIngredientAmount;

public class UpdateBoMIngredientAmountRequest : IRequest<UpdateBoMIngredientAmountResponse>
{
    [Required] public string ProductCode { get; set; } = null!;
    [Required] public string IngredientCode { get; set; } = null!;
    public double NewAmount { get; set; }
}
```

- [ ] **Step 13.2: Create response**

```csharp
// UpdateBoMIngredientAmountResponse.cs
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateBoMIngredientAmount;

public class UpdateBoMIngredientAmountResponse : BaseResponse
{
    public string? UserMessage { get; set; }

    public UpdateBoMIngredientAmountResponse() : base() { }
    public UpdateBoMIngredientAmountResponse(Exception ex) : base(ex) { }
}
```

- [ ] **Step 13.3: Create handler**

```csharp
// UpdateBoMIngredientAmountHandler.cs
using Anela.Heblo.Application.Features.Manufacture.ErrorFilters;
using Anela.Heblo.Domain.Features.Manufacture;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateBoMIngredientAmount;

public class UpdateBoMIngredientAmountHandler
    : IRequestHandler<UpdateBoMIngredientAmountRequest, UpdateBoMIngredientAmountResponse>
{
    private readonly IManufactureClient _manufactureClient;
    private readonly IManufactureErrorTransformer _errorTransformer;
    private readonly ILogger<UpdateBoMIngredientAmountHandler> _logger;

    public UpdateBoMIngredientAmountHandler(
        IManufactureClient manufactureClient,
        IManufactureErrorTransformer errorTransformer,
        ILogger<UpdateBoMIngredientAmountHandler> logger)
    {
        _manufactureClient = manufactureClient;
        _errorTransformer = errorTransformer;
        _logger = logger;
    }

    public async Task<UpdateBoMIngredientAmountResponse> Handle(
        UpdateBoMIngredientAmountRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await _manufactureClient.UpdateBoMIngredientAmountAsync(
                request.ProductCode,
                request.IngredientCode,
                request.NewAmount,
                cancellationToken);

            _logger.LogInformation(
                "Updated BoM ingredient amount: product {ProductCode} ingredient {IngredientCode} = {NewAmount}",
                request.ProductCode, request.IngredientCode, request.NewAmount);

            return new UpdateBoMIngredientAmountResponse();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Failed to update BoM ingredient amount: product {ProductCode} ingredient {IngredientCode}",
                request.ProductCode, request.IngredientCode);

            return new UpdateBoMIngredientAmountResponse(ex)
            {
                UserMessage = _errorTransformer.Transform(ex)
            };
        }
    }
}
```

- [ ] **Step 13.4: Write tests for the handler**

```csharp
// backend/test/Anela.Heblo.Tests/Features/Manufacture/UseCases/UpdateBoMIngredientAmountHandlerTests.cs
using Anela.Heblo.Application.Features.Manufacture.ErrorFilters;
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateBoMIngredientAmount;
using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.UseCases;

public class UpdateBoMIngredientAmountHandlerTests
{
    private readonly Mock<IManufactureClient> _clientMock = new();
    private readonly Mock<IManufactureErrorTransformer> _transformerMock = new();
    private readonly Mock<ILogger<UpdateBoMIngredientAmountHandler>> _loggerMock = new();
    private readonly UpdateBoMIngredientAmountHandler _handler;

    public UpdateBoMIngredientAmountHandlerTests()
    {
        _handler = new UpdateBoMIngredientAmountHandler(
            _clientMock.Object, _transformerMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_WhenClientSucceeds_ReturnsSuccess()
    {
        var request = new UpdateBoMIngredientAmountRequest
        {
            ProductCode = "PROD-1",
            IngredientCode = "ING-1",
            NewAmount = 12.5
        };

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        _clientMock.Verify(
            c => c.UpdateBoMIngredientAmountAsync("PROD-1", "ING-1", 12.5, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenClientThrows_ReturnsErrorResponseWithUserMessage()
    {
        _clientMock
            .Setup(c => c.UpdateBoMIngredientAmountAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Flexi error"));
        _transformerMock
            .Setup(t => t.Transform(It.IsAny<Exception>()))
            .Returns("Nepodařilo se aktualizovat složení BoM.");

        var result = await _handler.Handle(
            new UpdateBoMIngredientAmountRequest { ProductCode = "P", IngredientCode = "I", NewAmount = 1 },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.UserMessage.Should().Be("Nepodařilo se aktualizovat složení BoM.");
    }

    [Fact]
    public async Task Handle_WhenCancelled_PropagatesOperationCanceledException()
    {
        _clientMock
            .Setup(c => c.UpdateBoMIngredientAmountAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var act = async () => await _handler.Handle(
            new UpdateBoMIngredientAmountRequest { ProductCode = "P", IngredientCode = "I", NewAmount = 1 },
            CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
```

- [ ] **Step 13.5: Run handler tests (should all pass)**

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UpdateBoMIngredientAmountHandler"
```

Expected: all 3 pass.

- [ ] **Step 13.6: Replace direct call in `ManufactureOrderApplicationService`**

In `ConfirmProductCompletionAsync` (current lines ~136–151), replace:

```csharp
await _manufactureClient.UpdateBoMIngredientAmountAsync(
    product.ProductCode,
    updateResult.Order!.SemiProduct.ProductCode,
    (double)product.AdjustedGramsPerUnit,
    cancellationToken);
```

with:

```csharp
var bomResponse = await _mediator.Send(
    new UpdateBoMIngredientAmountRequest
    {
        ProductCode = product.ProductCode,
        IngredientCode = updateResult.Order!.SemiProduct.ProductCode,
        NewAmount = (double)product.AdjustedGramsPerUnit
    },
    cancellationToken);

if (!bomResponse.Success)
{
    _logger.LogWarning(
        "Failed to update BoM ingredient amount for product {ProductCode} in order {OrderId}: {UserMessage}",
        product.ProductCode, orderId, bomResponse.UserMessage);
}
```

(The inner try/catch around the loop body is no longer needed because the handler absorbs exceptions; keep the partial-failure info in `bomResponse.UserMessage`.)

- [ ] **Step 13.7: Drop `IManufactureClient` from `ManufactureOrderApplicationService` constructor**

Search for `_manufactureClient` uses in the service:

```bash
cd backend
grep -n "_manufactureClient" \
  src/Anela.Heblo.Application/Features/Manufacture/Services/ManufactureOrderApplicationService.cs
```

Expected: zero hits after Step 13.6. Remove the field, constructor parameter, and the `using` statement if now unused.

- [ ] **Step 13.8: Update existing service tests**

`ManufactureOrderApplicationServiceTests.cs` likely injects `Mock<IManufactureClient>`. Remove it from the test's constructor and from any `BuildSut()`/`CreateService()` helper. Tests that previously asserted `_manufactureClientMock.Verify(...)` for BoM updates should now assert on `_mediatorMock.Verify(m => m.Send(It.IsAny<UpdateBoMIngredientAmountRequest>(), ...))`.

- [ ] **Step 13.9: Run service + handler tests**

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Manufacture"
```

Expected: all pass.

- [ ] **Step 13.10: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateBoMIngredientAmount/ \
        backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ManufactureOrderApplicationService.cs \
        backend/test/Anela.Heblo.Tests/Features/Manufacture/UseCases/UpdateBoMIngredientAmountHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/ManufactureOrderApplicationServiceTests.cs
git commit -m "refactor(manufacture): route BoM update through MediatR

ManufactureOrderApplicationService no longer takes IManufactureClient
directly — BoM updates now go through UpdateBoMIngredientAmountHandler,
restoring a consistent adapter boundary."
```

---

## Task 14: Extract `ManufactureNameBuilder`

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/ManufactureNameBuilder.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/Workflows/ManufactureNameBuilderTests.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ManufactureOrderApplicationService.cs`

- [ ] **Step 14.1: Create the builder**

```csharp
// ManufactureNameBuilder.cs
using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Application.Features.Manufacture.Services.Workflows;

internal interface IManufactureNameBuilder
{
    string Build(UpdateManufactureOrderDto order, ErpManufactureType type);
}

internal sealed class ManufactureNameBuilder : IManufactureNameBuilder
{
    private const int ProductCodePrefixLength = 6;
    private const int MaxManufactureNameLength = 40;

    private readonly IProductNameFormatter _nameFormatter;

    public ManufactureNameBuilder(IProductNameFormatter nameFormatter)
    {
        _nameFormatter = nameFormatter;
    }

    public string Build(UpdateManufactureOrderDto order, ErpManufactureType type)
    {
        var semiCode = order.SemiProduct.ProductCode;
        var shortName = _nameFormatter.ShortProductName(order.SemiProduct.ProductName);
        var prefix = SafeTake(semiCode, ProductCodePrefixLength);

        string name;
        if (type == ErpManufactureType.Product)
        {
            if (order.Products.All(p => p.ProductCode == semiCode))
            {
                name = semiCode;
            }
            else
            {
                name = $"{prefix} {shortName}";
            }
        }
        else
        {
            name = $"{prefix}M {shortName}";
        }

        return SafeTake(name, MaxManufactureNameLength);
    }

    private static string SafeTake(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }
}
```

- [ ] **Step 14.2: Write focused builder tests**

```csharp
// ManufactureNameBuilderTests.cs
using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Application.Features.Manufacture.Services;
using Anela.Heblo.Application.Features.Manufacture.Services.Workflows;
using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.Services.Workflows;

public class ManufactureNameBuilderTests
{
    private readonly Mock<IProductNameFormatter> _formatterMock = new();
    private readonly ManufactureNameBuilder _sut;

    public ManufactureNameBuilderTests()
    {
        _formatterMock.Setup(f => f.ShortProductName(It.IsAny<string>()))
                       .Returns<string>(s => s);
        _sut = new ManufactureNameBuilder(_formatterMock.Object);
    }

    [Fact]
    public void Build_WhenProductCodeShorterThan6Chars_DoesNotThrow()
    {
        var order = BuildOrder(semiCode: "ABC", semiName: "Short");

        var act = () => _sut.Build(order, ErpManufactureType.SemiProduct);

        act.Should().NotThrow();
    }

    [Fact]
    public void Build_WhenSemiProduct_PrependsMSuffix()
    {
        var order = BuildOrder(semiCode: "CODE12", semiName: "Bisabolol");

        var result = _sut.Build(order, ErpManufactureType.SemiProduct);

        result.Should().Be("CODE12M Bisabolol");
    }

    [Fact]
    public void Build_WhenSinglephaseProduct_ReturnsSemiCodeOnly()
    {
        var order = BuildOrder(semiCode: "PROD01", semiName: "Single");
        order.Products.Add(new UpdateManufactureOrderProductDto { ProductCode = "PROD01" });

        var result = _sut.Build(order, ErpManufactureType.Product);

        result.Should().Be("PROD01");
    }

    [Fact]
    public void Build_WhenResultExceeds40Chars_TruncatesSafely()
    {
        var order = BuildOrder(
            semiCode: "CODE12",
            semiName: "A very long name that combined will easily exceed the forty character limit");

        var result = _sut.Build(order, ErpManufactureType.SemiProduct);

        result.Length.Should().BeLessThanOrEqualTo(40);
    }

    [Fact]
    public void Build_WhenCodeShorterThanPrefix_UsesFullCode()
    {
        var order = BuildOrder(semiCode: "AB", semiName: "X");

        var result = _sut.Build(order, ErpManufactureType.SemiProduct);

        result.Should().StartWith("AB");
    }

    private static UpdateManufactureOrderDto BuildOrder(string semiCode, string semiName) => new()
    {
        Id = 1,
        SemiProduct = new UpdateManufactureOrderSemiProductDto
        {
            ProductCode = semiCode,
            ProductName = semiName,
        },
        Products = new List<UpdateManufactureOrderProductDto>()
    };
}
```

Match the real shape of `UpdateManufactureOrderDto` / `UpdateManufactureOrderSemiProductDto` / `UpdateManufactureOrderProductDto` — read the contract files to confirm property names.

- [ ] **Step 14.3: Register and inject the builder**

In `ManufactureModule.cs`:

```csharp
services.AddScoped<IManufactureNameBuilder, ManufactureNameBuilder>();
```

In `ManufactureOrderApplicationService`:
1. Add `IManufactureNameBuilder _nameBuilder` field + constructor param.
2. Drop the `IProductNameFormatter _productNameFormatter` field and param (it moves into the builder).
3. Replace the `CreateManufactureName` method's entire body with `return _nameBuilder.Build(order, type);` — or inline the call and delete `CreateManufactureName` entirely, updating its two call sites.

- [ ] **Step 14.4: Remove temporary tests added in Task 3**

Delete the three edge-case tests added to `ManufactureOrderApplicationServiceTests.cs` in Task 3.1 — they are now covered by the focused `ManufactureNameBuilderTests`.

- [ ] **Step 14.5: Run tests**

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Manufacture"
```

Expected: all pass, including 5 new `ManufactureNameBuilderTests`.

- [ ] **Step 14.6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/ \
        backend/test/Anela.Heblo.Tests/Features/Manufacture/
git commit -m "refactor(manufacture): extract ManufactureNameBuilder"
```

---

## Task 15: Extract Czech messages into `ManufactureMessages`

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureMessages.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ManufactureOrderApplicationService.cs`

- [ ] **Step 15.1: Create the message class**

```csharp
// ManufactureMessages.cs
namespace Anela.Heblo.Application.Features.Manufacture;

internal static class ManufactureMessages
{
    public const string UnexpectedSemiProductError =
        "Došlo k neočekávané chybě při potvrzení výroby polotovaru";

    public const string UnexpectedProductCompletionErrorFormat =
        "Došlo k neočekávané chybě při dokončení výroby produktů: {0}";

    public const string QuantityUpdateErrorFormat =
        "Chyba při aktualizaci množství: {0}";

    public const string ProductQuantityUpdateErrorFormat =
        "Chyba při aktualizaci množství produktů: {0}";

    public const string StatusChangeErrorFormat =
        "Chyba při změně stavu: {0}";

    public const string SemiProductManufacturedSuccessFormat =
        "Polotovar byl úspěšně vyroben se skutečným množstvím {0}";

    public const string SemiProductDefaultChangeReasonFormat =
        "Potvrzeno skutečné množství polotovaru: {0}";

    public const string SemiProductErpNoteFormat =
        "Vytvořena vydaná objednávka meziproduktu {0}";

    public const string ProductCompletionDefaultChangeReason =
        "Potvrzeno dokončení výroby produktů";

    public const string ProductCompletionDefaultNoteFormat =
        "Potvrzeno dokončení výroby produktů - {0}";

    public const string WeightToleranceOverrideFormat =
        "Hmotnost mimo toleranci potvrzena uživatelem. Rozdíl: {0:F2}% (povoleno: {1:F2}%)";
}
```

- [ ] **Step 15.2: Replace inline strings in `ManufactureOrderApplicationService.cs`**

Use `string.Format(ManufactureMessages.XxxFormat, ...)` for formatted messages and direct references for literal constants. For example:

```csharp
// Before:
return new ConfirmSemiProductManufactureResult(false, "Došlo k neočekávané chybě při potvrzení výroby polotovaru");

// After:
return new ConfirmSemiProductManufactureResult(false, ManufactureMessages.UnexpectedSemiProductError);
```

Replace every Czech string literal currently in the service (there are ~10).

- [ ] **Step 15.3: Run tests**

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Manufacture"
```

Expected: all pass. Tests that assert on exact message text remain valid because the constants contain the same strings.

- [ ] **Step 15.4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/
git commit -m "refactor(manufacture): centralize Czech user messages in ManufactureMessages"
```

---

## Task 16: Extract `ConfirmSemiProductManufactureWorkflow`

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/ConfirmSemiProductManufactureWorkflow.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/Workflows/ConfirmSemiProductManufactureWorkflowTests.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ManufactureOrderApplicationService.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureModule.cs`

- [ ] **Step 16.1: Define interface + workflow class**

```csharp
// ConfirmSemiProductManufactureWorkflow.cs
using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Application.Features.Manufacture.UseCases.SubmitManufacture;
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrder;
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrderStatus;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Manufacture.Services.Workflows;

internal interface IConfirmSemiProductManufactureWorkflow
{
    Task<ConfirmSemiProductManufactureResult> ExecuteAsync(
        int orderId,
        decimal actualQuantity,
        string? changeReason,
        CancellationToken cancellationToken);
}

internal sealed class ConfirmSemiProductManufactureWorkflow : IConfirmSemiProductManufactureWorkflow
{
    private readonly IMediator _mediator;
    private readonly IManufactureNameBuilder _nameBuilder;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<ConfirmSemiProductManufactureWorkflow> _logger;

    public ConfirmSemiProductManufactureWorkflow(
        IMediator mediator,
        IManufactureNameBuilder nameBuilder,
        TimeProvider timeProvider,
        ICurrentUserService currentUserService,
        ILogger<ConfirmSemiProductManufactureWorkflow> logger)
    {
        _mediator = mediator;
        _nameBuilder = nameBuilder;
        _timeProvider = timeProvider;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<ConfirmSemiProductManufactureResult> ExecuteAsync(
        int orderId,
        decimal actualQuantity,
        string? changeReason,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Starting semi-product manufacture confirmation for order {OrderId} with quantity {ActualQuantity}",
                orderId, actualQuantity);

            var updateResult = await UpdateSemiProductQuantityAsync(orderId, actualQuantity, cancellationToken);
            if (!updateResult.Success)
            {
                return new ConfirmSemiProductManufactureResult(false,
                    string.Format(ManufactureMessages.QuantityUpdateErrorFormat, updateResult.ErrorCode));
            }

            var submitResult = await SubmitToErpAsync(orderId, updateResult.Order!, cancellationToken);

            var statusResult = await UpdateStatusAsync(orderId, actualQuantity, changeReason, submitResult, cancellationToken);
            if (!statusResult.Success)
            {
                return new ConfirmSemiProductManufactureResult(false,
                    string.Format(ManufactureMessages.StatusChangeErrorFormat, statusResult.ErrorCode));
            }

            return new ConfirmSemiProductManufactureResult(true,
                string.Format(ManufactureMessages.SemiProductManufacturedSuccessFormat, actualQuantity));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error confirming semi-product manufacture for order {OrderId}", orderId);
            return new ConfirmSemiProductManufactureResult(false, ManufactureMessages.UnexpectedSemiProductError);
        }
    }

    private async Task<UpdateManufactureOrderResponse> UpdateSemiProductQuantityAsync(
        int orderId, decimal actualQuantity, CancellationToken cancellationToken)
    {
        var request = new UpdateManufactureOrderRequest
        {
            Id = orderId,
            SemiProduct = new UpdateManufactureOrderSemiProductRequest { ActualQuantity = actualQuantity }
        };
        return await _mediator.Send(request, cancellationToken);
    }

    private async Task<SubmitManufactureResponse> SubmitToErpAsync(
        int orderId, UpdateManufactureOrderDto order, CancellationToken cancellationToken)
    {
        var semi = order.SemiProduct;
        var request = new SubmitManufactureRequest
        {
            ManufactureOrderNumber = order.OrderNumber,
            ManufactureInternalNumber = _nameBuilder.Build(order, ErpManufactureType.SemiProduct),
            ManufactureType = ErpManufactureType.SemiProduct,
            Date = _timeProvider.GetUtcNow().DateTime,
            CreatedBy = _currentUserService.GetCurrentUser().Name,
            Items = new List<SubmitManufactureRequestItem>
            {
                new()
                {
                    ProductCode = semi.ProductCode,
                    Name = semi.ProductName,
                    Amount = semi.ActualQuantity ?? semi.PlannedQuantity,
                }
            },
            LotNumber = semi.LotNumber,
            ExpirationDate = semi.ExpirationDate,
            ResidueDistribution = null,
        };
        var result = await _mediator.Send(request, cancellationToken);
        if (!result.Success)
        {
            _logger.LogError("Failed to create manufacture for order {OrderId}: {ErrorCode}",
                orderId, result.ErrorCode);
        }
        else
        {
            _logger.LogInformation("Successfully created manufacture {ManufactureId} for order {OrderId}",
                result.ManufactureId, orderId);
        }
        return result;
    }

    private async Task<UpdateManufactureOrderStatusResponse> UpdateStatusAsync(
        int orderId,
        decimal actualQuantity,
        string? changeReason,
        SubmitManufactureResponse submitResult,
        CancellationToken cancellationToken)
    {
        var note = submitResult.Success
            ? string.Format(ManufactureMessages.SemiProductErpNoteFormat, submitResult.ManufactureId)
            : submitResult.UserMessage ?? submitResult.FullError();

        var command = new UpdateOrderStatusCommand(
            OrderId: orderId,
            TargetState: ManufactureOrderState.SemiProductManufactured,
            ChangeReason: changeReason ?? string.Format(ManufactureMessages.SemiProductDefaultChangeReasonFormat, actualQuantity),
            Note: note,
            Documents: new ManufactureDocumentCodes(
                SemiProduct: submitResult.ManufactureId,
                Product: null,
                Discard: null),
            ManualActionRequired: !submitResult.Success,
            WeightTolerance: null);

        return await _mediator.Send(new UpdateManufactureOrderStatusRequest
        {
            Id = command.OrderId,
            NewState = command.TargetState,
            ChangeReason = command.ChangeReason,
            Note = command.Note,
            SemiProductOrderCode = command.Documents.SemiProduct,
            ProductOrderCode = command.Documents.Product,
            DiscardRedisueDocumentCode = command.Documents.Discard,
            ManualActionRequired = command.ManualActionRequired,
            WeightWithinTolerance = command.WeightTolerance?.WithinTolerance,
            WeightDifference = command.WeightTolerance?.Difference,
        }, cancellationToken);
    }
}
```

- [ ] **Step 16.2: Write workflow unit tests**

```csharp
// ConfirmSemiProductManufactureWorkflowTests.cs
// Mirror style of existing ManufactureOrderApplicationServiceTests.cs.
// Test cases:
//  - HappyPath_ReturnsSuccess
//  - WhenUpdateQuantityFails_ReturnsQuantityUpdateError
//  - WhenErpSubmitFails_StillTransitionsStateWithManualActionRequired
//  - WhenStatusUpdateFailsAfterErpSucceeds_ReturnsStatusChangeError
//  - WhenCancelled_PropagatesOperationCanceledException
//  - WhenUnexpectedException_ReturnsGenericMessage
```

Write each test with full Moq setup and FluentAssertions. Use the existing service tests as templates — the workflow is a straight extraction of the method body.

- [ ] **Step 16.3: Delegate from `ManufactureOrderApplicationService`**

In the service:
1. Add `IConfirmSemiProductManufactureWorkflow _semiProductWorkflow` field + ctor param.
2. Replace the entire body of `ConfirmSemiProductManufactureAsync` with:

```csharp
public Task<ConfirmSemiProductManufactureResult> ConfirmSemiProductManufactureAsync(
    int orderId,
    decimal actualQuantity,
    string? changeReason = null,
    CancellationToken cancellationToken = default)
{
    return _semiProductWorkflow.ExecuteAsync(orderId, actualQuantity, changeReason, cancellationToken);
}
```

3. Delete the now-unused `UpdateSemiProductQuantity` private method.

- [ ] **Step 16.4: Register in DI**

In `ManufactureModule.cs`:

```csharp
services.AddScoped<IConfirmSemiProductManufactureWorkflow, ConfirmSemiProductManufactureWorkflow>();
```

- [ ] **Step 16.5: Run tests**

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Manufacture"
```

Expected: all existing service tests still pass (since behaviour is preserved) plus the 6 new workflow tests.

- [ ] **Step 16.6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/ \
        backend/test/Anela.Heblo.Tests/Features/Manufacture/
git commit -m "refactor(manufacture): extract ConfirmSemiProductManufactureWorkflow"
```

---

## Task 17: Extract `ConfirmProductCompletionWorkflow` and surface BoM failures

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/ConfirmProductCompletionWorkflow.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/Workflows/ConfirmProductCompletionWorkflowTests.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ManufactureOrderApplicationService.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureModule.cs`

- [ ] **Step 17.1: Define the workflow (split 102-line method into explicit steps)**

```csharp
// ConfirmProductCompletionWorkflow.cs
using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Application.Features.Manufacture.UseCases.SubmitManufacture;
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateBoMIngredientAmount;
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrder;
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrderStatus;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Manufacture.Services.Workflows;

internal interface IConfirmProductCompletionWorkflow
{
    Task<ConfirmProductCompletionResult> ExecuteAsync(
        int orderId,
        Dictionary<int, decimal> productActualQuantities,
        bool overrideConfirmed,
        string? changeReason,
        CancellationToken cancellationToken);
}

internal sealed class ConfirmProductCompletionWorkflow : IConfirmProductCompletionWorkflow
{
    private readonly IMediator _mediator;
    private readonly IResidueDistributionCalculator _residueCalculator;
    private readonly IManufactureNameBuilder _nameBuilder;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<ConfirmProductCompletionWorkflow> _logger;

    public ConfirmProductCompletionWorkflow(
        IMediator mediator,
        IResidueDistributionCalculator residueCalculator,
        IManufactureNameBuilder nameBuilder,
        TimeProvider timeProvider,
        ICurrentUserService currentUserService,
        ILogger<ConfirmProductCompletionWorkflow> logger)
    {
        _mediator = mediator;
        _residueCalculator = residueCalculator;
        _nameBuilder = nameBuilder;
        _timeProvider = timeProvider;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<ConfirmProductCompletionResult> ExecuteAsync(
        int orderId,
        Dictionary<int, decimal> productActualQuantities,
        bool overrideConfirmed,
        string? changeReason,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Starting product completion confirmation for order {OrderId} with {ProductCount} products",
                orderId, productActualQuantities.Count);

            // Step 1
            var updateResult = await UpdateProductsQuantityAsync(orderId, productActualQuantities, cancellationToken);
            if (!updateResult.Success)
            {
                return new ConfirmProductCompletionResult(
                    string.Format(ManufactureMessages.ProductQuantityUpdateErrorFormat, updateResult.ErrorCode));
            }

            // Step 2
            var distribution = await _residueCalculator.CalculateAsync(updateResult.Order!, cancellationToken);

            // Step 3 — threshold gate
            if (!distribution.IsWithinAllowedThreshold && !overrideConfirmed)
            {
                _logger.LogInformation(
                    "Order {OrderId} requires user confirmation: residue {DiffPct:F2}% exceeds allowed {AllowedPct:F2}%",
                    orderId, distribution.DifferencePercentage, distribution.AllowedResiduePercentage);
                return ConfirmProductCompletionResult.NeedsConfirmation(distribution);
            }

            // Step 4
            var submitResult = await SubmitToErpAsync(orderId, updateResult.Order!, distribution, cancellationToken);

            // Step 5 — BoM updates; collect failures instead of swallowing
            var bomFailures = await UpdateBoMIngredientsAsync(submitResult, updateResult.Order!, distribution, orderId, cancellationToken);

            // Step 6 — state transition with combined note
            var statusResult = await TransitionToCompletedAsync(
                orderId, submitResult, distribution, overrideConfirmed, changeReason, bomFailures, cancellationToken);
            if (!statusResult.Success)
            {
                return new ConfirmProductCompletionResult(
                    string.Format(ManufactureMessages.StatusChangeErrorFormat, statusResult.ErrorCode));
            }

            return new ConfirmProductCompletionResult();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error confirming product completion for order {OrderId}", orderId);
            return new ConfirmProductCompletionResult(
                string.Format(ManufactureMessages.UnexpectedProductCompletionErrorFormat, ex.Message));
        }
    }

    private async Task<UpdateManufactureOrderResponse> UpdateProductsQuantityAsync(
        int orderId,
        Dictionary<int, decimal> productActualQuantities,
        CancellationToken cancellationToken)
    {
        var productRequests = productActualQuantities.Select(kvp => new UpdateManufactureOrderProductRequest
        {
            Id = kvp.Key,
            ActualQuantity = kvp.Value,
        }).ToList();

        return await _mediator.Send(new UpdateManufactureOrderRequest
        {
            Id = orderId,
            Products = productRequests
        }, cancellationToken);
    }

    private async Task<SubmitManufactureResponse> SubmitToErpAsync(
        int orderId,
        UpdateManufactureOrderDto order,
        ResidueDistribution distribution,
        CancellationToken cancellationToken)
    {
        var request = new SubmitManufactureRequest
        {
            ManufactureOrderNumber = order.OrderNumber,
            ManufactureInternalNumber = _nameBuilder.Build(order, ErpManufactureType.Product),
            ManufactureType = ErpManufactureType.Product,
            Date = _timeProvider.GetUtcNow().DateTime,
            CreatedBy = _currentUserService.GetCurrentUser().Name,
            Items = order.Products.Select(p => new SubmitManufactureRequestItem
            {
                ProductCode = p.ProductCode,
                Name = p.ProductName,
                Amount = p.ActualQuantity ?? p.PlannedQuantity,
            }).ToList(),
            LotNumber = order.SemiProduct.LotNumber,
            ExpirationDate = order.SemiProduct.ExpirationDate,
            ResidueDistribution = distribution,
        };

        var result = await _mediator.Send(request, cancellationToken);
        if (!result.Success)
        {
            _logger.LogError("Failed to create manufacture for order {OrderId}: {ErrorCode}",
                orderId, result.ErrorCode);
        }
        else
        {
            _logger.LogInformation("Successfully created manufacture {ManufactureId} for order {OrderId}",
                result.ManufactureId, orderId);
        }
        return result;
    }

    private async Task<List<string>> UpdateBoMIngredientsAsync(
        SubmitManufactureResponse submitResult,
        UpdateManufactureOrderDto order,
        ResidueDistribution distribution,
        int orderId,
        CancellationToken cancellationToken)
    {
        var failures = new List<string>();
        if (!submitResult.Success)
            return failures;

        foreach (var product in distribution.Products)
        {
            var response = await _mediator.Send(new UpdateBoMIngredientAmountRequest
            {
                ProductCode = product.ProductCode,
                IngredientCode = order.SemiProduct.ProductCode,
                NewAmount = (double)product.AdjustedGramsPerUnit,
            }, cancellationToken);

            if (!response.Success)
            {
                _logger.LogWarning(
                    "Failed to update BoM ingredient amount for product {ProductCode} in order {OrderId}: {UserMessage}",
                    product.ProductCode, orderId, response.UserMessage);
                failures.Add($"{product.ProductCode}: {response.UserMessage ?? response.FullError()}");
            }
        }
        return failures;
    }

    private async Task<UpdateManufactureOrderStatusResponse> TransitionToCompletedAsync(
        int orderId,
        SubmitManufactureResponse submitResult,
        ResidueDistribution distribution,
        bool overrideConfirmed,
        string? changeReason,
        List<string> bomFailures,
        CancellationToken cancellationToken)
    {
        string? orderNote = null;
        if (!submitResult.Success)
            orderNote = submitResult.UserMessage ?? submitResult.FullError();

        string? weightToleranceNote = null;
        if (overrideConfirmed && !distribution.IsWithinAllowedThreshold)
        {
            weightToleranceNote = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                ManufactureMessages.WeightToleranceOverrideFormat,
                distribution.DifferencePercentage,
                distribution.AllowedResiduePercentage);
        }

        string? bomFailureNote = null;
        if (bomFailures.Count > 0)
            bomFailureNote = "BoM update failures: " + string.Join("; ", bomFailures);

        var combinedNote = string.Join("\n",
            new[] { orderNote, weightToleranceNote, bomFailureNote }.Where(n => !string.IsNullOrEmpty(n)));

        if (string.IsNullOrEmpty(combinedNote))
        {
            combinedNote = string.Format(ManufactureMessages.ProductCompletionDefaultNoteFormat, submitResult.ManufactureId);
        }

        var manualActionRequired = !submitResult.Success || bomFailures.Count > 0;

        var request = new UpdateManufactureOrderStatusRequest
        {
            Id = orderId,
            NewState = ManufactureOrderState.Completed,
            ChangeReason = changeReason ?? ManufactureMessages.ProductCompletionDefaultChangeReason,
            Note = combinedNote,
            SemiProductOrderCode = null,
            ProductOrderCode = submitResult.ManufactureId,
            DiscardRedisueDocumentCode = null,
            ManualActionRequired = manualActionRequired,
            WeightWithinTolerance = distribution.IsWithinAllowedThreshold,
            WeightDifference = distribution.Difference,
        };

        return await _mediator.Send(request, cancellationToken);
    }
}
```

Key behavior change from the original: **BoM failures are now collected and surfaced on the order note with `ManualActionRequired = true`**, rather than silently swallowed into warning logs.

- [ ] **Step 17.2: Write workflow tests (minimum 8)**

```csharp
// ConfirmProductCompletionWorkflowTests.cs — case list:
// 1. HappyPath_AllStepsSucceed_ReturnsSuccess
// 2. WhenUpdateProductsFails_ReturnsQuantityUpdateError
// 3. WhenResidueExceedsThresholdAndNotOverridden_ReturnsNeedsConfirmation
// 4. WhenResidueExceedsThresholdButOverridden_AppendsWeightToleranceNote
// 5. WhenErpSubmitFails_StillTransitionsWithManualActionRequired
// 6. WhenBoMUpdateFails_AppendsFailureNoteAndSetsManualActionRequired   // NEW BEHAVIOR
// 7. WhenStatusUpdateFailsAfterErpSucceeds_ReturnsStatusChangeError
// 8. WhenCancelled_PropagatesOperationCanceledException
```

For test 6 specifically (the new behavior), the assertion should verify that:
- `UpdateManufactureOrderStatusRequest` sent to mediator has `ManualActionRequired = true`
- `Note` contains the product code(s) that failed BoM update
- The final result is success (the workflow completes, even though BoM updates failed — the operator is alerted via the order note)

Example for test 6:

```csharp
[Fact]
public async Task ExecuteAsync_WhenBoMUpdateFails_AppendsFailureNoteAndSetsManualActionRequired()
{
    // Arrange
    var order = BuildOrder();
    ArrangeUpdateProductsSuccess(order);
    ArrangeResidueWithinTolerance();
    ArrangeSubmitErpSuccess("M-1");

    _mediatorMock
        .Setup(m => m.Send(It.IsAny<UpdateBoMIngredientAmountRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new UpdateBoMIngredientAmountResponse(new InvalidOperationException("flexi err"))
        {
            UserMessage = "BoM couldn't be updated"
        });

    UpdateManufactureOrderStatusRequest? capturedStatus = null;
    _mediatorMock
        .Setup(m => m.Send(It.IsAny<UpdateManufactureOrderStatusRequest>(), It.IsAny<CancellationToken>()))
        .Callback<IRequest<UpdateManufactureOrderStatusResponse>, CancellationToken>((req, _) =>
            capturedStatus = (UpdateManufactureOrderStatusRequest)req)
        .ReturnsAsync(new UpdateManufactureOrderStatusResponse { Success = true });

    // Act
    var result = await _sut.ExecuteAsync(
        order.Id,
        new Dictionary<int, decimal> { { 1, 10m } },
        overrideConfirmed: false,
        changeReason: null,
        CancellationToken.None);

    // Assert
    result.Success.Should().BeTrue();
    capturedStatus.Should().NotBeNull();
    capturedStatus!.ManualActionRequired.Should().BeTrue();
    capturedStatus.Note.Should().Contain("BoM update failures");
}
```

- [ ] **Step 17.3: Delegate from `ManufactureOrderApplicationService`**

Inject `IConfirmProductCompletionWorkflow _productCompletionWorkflow` and reduce `ConfirmProductCompletionAsync` to:

```csharp
public Task<ConfirmProductCompletionResult> ConfirmProductCompletionAsync(
    int orderId,
    Dictionary<int, decimal> productActualQuantities,
    bool overrideConfirmed = false,
    string? changeReason = null,
    CancellationToken cancellationToken = default)
{
    return _productCompletionWorkflow.ExecuteAsync(orderId, productActualQuantities, overrideConfirmed, changeReason, cancellationToken);
}
```

Delete `UpdateProductsQuantity`, `CreateManufactureOrderInErp`, and the unused `_timeProvider`/`_currentUserService`/`_residueCalculator` fields if the service no longer uses them (the workflow owns them now). Drop their constructor parameters.

- [ ] **Step 17.4: Register workflow in DI**

In `ManufactureModule.cs`:

```csharp
services.AddScoped<IConfirmProductCompletionWorkflow, ConfirmProductCompletionWorkflow>();
```

- [ ] **Step 17.5: Update service tests**

The existing `ManufactureOrderApplicationServiceTests.cs` contains 13 tests. Most of them validate workflow behavior that now lives in the workflow tests. Either:
- Keep them as higher-level integration-ish tests against the service (which delegates), or
- Move the behavior tests into the workflow test class and reduce the service tests to pure delegation checks (`_workflowMock.Verify(...)`).

Recommended: **move behavior tests** to the workflow test class, leave ~3 delegation tests in the service test class. This avoids duplication.

- [ ] **Step 17.6: Run full manufacture test suite**

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Manufacture"
```

Expected: all pass. `ManufactureOrderApplicationService.cs` should now be ~80 LOC.

- [ ] **Step 17.7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/ \
        backend/test/Anela.Heblo.Tests/Features/Manufacture/
git commit -m "refactor(manufacture): extract ConfirmProductCompletionWorkflow

Decompose the 102-line ConfirmProductCompletionAsync into explicit
steps (UpdateProducts, CalculateResidue, ThresholdGate, SubmitToErp,
UpdateBoM, TransitionToCompleted).

Behavior change: BoM update failures are now collected and appended to
the order note with ManualActionRequired=true, instead of silently
disappearing into warning logs."
```

---

# Phase 6 — Final Cleanup

## Task 18: Remove duplicate try/catches from `ManufactureOrderController`

**Files:**
- Read: `backend/src/Anela.Heblo.API/Controllers/ManufactureOrderController.cs`
- Check: existence of global exception-handling middleware

- [ ] **Step 18.1: Verify global middleware handles unhandled exceptions**

```bash
cd backend
grep -rn "UseExceptionHandler\|IExceptionHandler\|ExceptionHandling" src/Anela.Heblo.API/
```

If a global handler exists and writes a consistent error response shape, proceed to 18.2. If none exists, **skip this task** — the controller try/catches are the only safety net.

- [ ] **Step 18.2: Read the two Confirm endpoints**

Read `ManufactureOrderController.cs` around lines 110–160. Identify the try/catch blocks wrapping calls to `_applicationService.ConfirmSemiProductManufactureAsync` and `ConfirmProductCompletionAsync`.

- [ ] **Step 18.3: Remove the try/catches (only if 18.1 confirmed global middleware)**

Since the workflow classes already catch non-cancellation exceptions and return `Result` objects with user messages, the controller try/catch is redundant. Remove it — return `Ok(result)` directly if `result.Success`, otherwise return `BadRequest(result)`.

- [ ] **Step 18.4: Run controller tests**

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ManufactureOrderController"
```

Expected: all pass.

- [ ] **Step 18.5: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/ManufactureOrderController.cs
git commit -m "refactor(manufacture): remove redundant controller try/catch

Workflow classes now return structured Result objects; exception path
is covered by global exception middleware."
```

---

## Task 19: Final suite run + dotnet format

- [ ] **Step 19.1: Run full backend suite**

```bash
cd backend
dotnet format --verify-no-changes
dotnet build
dotnet test
```

Expected: format clean, build succeeds, all tests pass.

- [ ] **Step 19.2: If `dotnet format --verify-no-changes` fails, apply formatting**

```bash
cd backend
dotnet format
git add -u
git commit -m "chore: apply dotnet format"
```

- [ ] **Step 19.3: Measure final LOC**

```bash
cd backend
wc -l \
  src/Anela.Heblo.Application/Features/Manufacture/UseCases/SubmitManufacture/SubmitManufactureHandler.cs \
  src/Anela.Heblo.Application/Features/Manufacture/Services/ManufactureOrderApplicationService.cs \
  src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureClient.cs
```

Expected approximate targets:
- `SubmitManufactureHandler.cs`: ~50 LOC (was 67)
- `ManufactureOrderApplicationService.cs`: ~80 LOC (was 350)
- `FlexiManufactureClient.cs`: ~200 LOC (was 781)

If any is significantly larger than target, investigate which extraction left code behind.

---

## Task 20: Manual smoke verification on staging

This task covers end-to-end behavior that unit tests can't — it must be run manually against staging because it touches the real Flexi ERP.

**Prerequisites:** merge the branch to staging first (normal PR flow), or use the staging environment variable override if this codebase supports running a feature branch there.

- [ ] **Step 20.1: Semi-product manufacture happy path**

In the staging UI:
1. Find a known test manufacture order in state `Planned` with a semi-product.
2. Trigger "Confirm semi-product manufacture" with an actual quantity that matches the planned quantity.
3. **Verify in Flexi:** new consumption movement appears for all ingredients; new production movement appears for the semi-product.
4. **Verify in Heblo:** order state is `SemiProductManufactured`, order note contains the ERP manufacture ID, `ManualActionRequired` is false.

- [ ] **Step 20.2: Product completion happy path (within tolerance)**

1. Find an order in state `SemiProductManufactured` with 2–3 products.
2. Trigger "Confirm product completion" with actual quantities that keep the residue within the allowed threshold.
3. **Verify in Flexi:** one consolidated consumption document appears covering all products; one production document appears with all products.
4. **Verify in Heblo:** state is `Completed`, `WeightWithinTolerance=true`, BoM updates reflected in subsequent template fetches.

- [ ] **Step 20.3: Product completion — threshold gate**

1. Submit actual quantities that exceed the threshold, with `overrideConfirmed=false`.
2. **Expected response:** `NeedsConfirmation` with distribution info; order state unchanged.
3. Re-submit with `overrideConfirmed=true`.
4. **Expected:** state transitions to `Completed`, order note contains the "Hmotnost mimo toleranci" message, `WeightWithinTolerance=false`.

- [ ] **Step 20.4: ERP failure surfacing**

1. Deliberately induce an ERP failure (e.g., bad lot expiration date) and trigger confirmation.
2. **Expected:** state transitions to `Completed` but `ManualActionRequired=true`, note contains the Flexi error translated by `IManufactureErrorTransformer`.

- [ ] **Step 20.5: BoM update failure surfacing (the new behavior)**

1. Set up a test product where `UpdateBoMIngredientAmountAsync` will fail (e.g., product with no BoM header in Flexi).
2. Trigger product completion with valid quantities.
3. **Expected:**
   - State transitions to `Completed` (because ERP submit succeeded).
   - `ManualActionRequired=true`.
   - Note contains `"BoM update failures:"` followed by the failing product code.
   - **Previously:** the failure would have been silently swallowed into warning logs.

- [ ] **Step 20.6: Cancellation behavior**

Hit the endpoint then immediately cancel the HTTP request from the client. Verify the backend logs show `OperationCanceledException` propagating up (previously swallowed by the catch-all).

---

## Verification Summary

**After all tasks complete, the following must all be true:**

1. `cd backend && dotnet format --verify-no-changes` → clean
2. `cd backend && dotnet build` → succeeds
3. `cd backend && dotnet test` → all tests pass (expect ~25+ new tests on top of baseline)
4. Manual staging smoke (Task 20) → all 6 scenarios behave as specified, including the new BoM-failure surfacing behavior
5. `wc -l` on the three original files → approximate LOC targets hit
6. `grep -n "_ordersClient\|MapToFlexiItem\|SubmitManufactureAggregatedAsync\|SubmitConsumptionMovementsAsync\|SubmitProductionMovementAsync" backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureClient.cs` → zero hits (dead code removed)
7. `grep -n "IManufactureClient" backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ManufactureOrderApplicationService.cs` → zero hits (direct adapter dependency severed)

**Test count after refactor (approximate):**

| File | Baseline | After |
|---|---|---|
| `SubmitManufactureHandlerTests.cs` | 3 | 5 |
| `ManufactureOrderApplicationServiceTests.cs` | 13 | ~3 (delegation only; behavior moved) |
| `ConfirmSemiProductManufactureWorkflowTests.cs` | new | 6 |
| `ConfirmProductCompletionWorkflowTests.cs` | new | 8 |
| `ManufactureNameBuilderTests.cs` | new | 5 |
| `UpdateBoMIngredientAmountHandlerTests.cs` | new | 3 |
| `FlexiManufactureClientTests.cs` | ~26 | ~28 (2 uncommented failure tests) |
| `FefoConsumptionAllocatorTests.cs` | new | 4 |
| `FlexiIngredientStockValidatorTests.cs` | new | 1+ |

**Net file count change:** roughly +15 new files, 0 deleted (all three original files remain, just smaller).

---

## Risks & Mitigations

| Risk | Mitigation |
|---|---|
| Consolidated path behaves differently than legacy for semi-products (Task 5) | Full adapter test suite runs after deletion; manual semi-product smoke in 20.1. |
| Moving behavior from service tests to workflow tests loses coverage | Task 17.5 requires moving tests, not deleting them. Line count before/after should be comparable. |
| `FlexiManufactureException` breaks the error transformer | Task 7.3 explicitly requires reading the transformer and adjusting filters. |
| BoM failure surfacing (Task 17) changes user-visible behavior | Explicitly called out in commit message and smoke test 20.5; this is the intended improvement. |
| Consumer code in the 11 other `IManufactureClient` call sites breaks | This plan leaves `IManufactureClient` surface unchanged — interface method count is identical. Only the implementation is split internally. |

---

## Scope Boundaries (explicitly OUT of scope)

- Changing `double` → `decimal` for money/quantity fields (cascades into Flexi SDK DTOs; separate task).
- Splitting `IManufactureClient` into multiple interfaces (aggressive-scope option; user chose moderate).
- Adding a MediatR validation pipeline behavior (no existing pipeline; separate task).
- Modifying any of the 11 other `IManufactureClient` consumers (batch planning, catalog, gift packages, etc.).
- Replacing `BaseResponse(Exception)` across the codebase — it's a shared pattern used far beyond manufacture.
- Localization infrastructure (Czech strings go into a constants class, not resx/i18n).
