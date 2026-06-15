# DuplicateManufactureOrderHandler Tests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a unit-test class that locks in the current behavior of `DuplicateManufactureOrderHandler` — source-not-found error, full duplication with `SemiProduct`, and duplication when `SemiProduct` is null — using the existing Manufacture-module test conventions.

**Architecture:** One new test file under `backend/test/Anela.Heblo.Tests/Features/Manufacture/`, flat layout matching every sibling `*HandlerTests.cs`. The handler is a transform; tests capture the `ManufactureOrder` passed to `IManufactureOrderRepository.AddOrderAsync` via a Moq `Callback` and assert on the captured instance with FluentAssertions. Time is faked with `Mock<TimeProvider>` (no new package), expected lot/expiration values are computed by calling the same public statics the handler calls (`ManufactureOrderExtensions.GetDefaultLot` / `GetDefaultExpiration`).

**Tech Stack:** xUnit, FluentAssertions, Moq, .NET 8, `Mock<TimeProvider>`. No production code is modified.

**Source under test:** `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/DuplicateManufactureOrder/DuplicateManufactureOrderHandler.cs`

**Conventions to match (verified before writing):**
- File location: flat at `backend/test/Anela.Heblo.Tests/Features/Manufacture/<Name>HandlerTests.cs` (mirrors `CreateManufactureOrderHandlerTests.cs`, `GetManufactureOrderHandlerTests.cs`, etc.).
- Class modifier: `public class` (not `sealed`) to match siblings.
- Mocking: Moq only.
- Assertions: FluentAssertions.
- TimeProvider: `Mock<TimeProvider>` stubbing `GetUtcNow()` — same pattern siblings use.
- `CurrentUser` ctor: `new CurrentUser("test-user-id", "Test User", "test@example.com", true)` — display name returned by `GetDisplayName()` is `"Test User"`.
- `CreatedDate` and `StateChangedAt` on the duplicated order come from `DateTime.UtcNow` inside the handler (not from `TimeProvider`). Tests must NOT assert on them — they are non-deterministic and out of scope.

---

### Task 1: Create the test file with constructor, constants, and the source-order fixture builder

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Manufacture/DuplicateManufactureOrderHandlerTests.cs`

- [ ] **Step 1: Verify the target path is empty and the sibling layout is flat**

Run from repo root:
```bash
ls backend/test/Anela.Heblo.Tests/Features/Manufacture/DuplicateManufactureOrderHandlerTests.cs 2>&1 | head
ls backend/test/Anela.Heblo.Tests/Features/Manufacture/ | head -20
```
Expected: first command prints `No such file or directory`. Second lists flat `*HandlerTests.cs` files only (no `UseCases/` subfolder). If a `UseCases/` directory exists, STOP and re-read the architecture review's "File location" decision.

- [ ] **Step 2: Create the file with using directives, namespace, class declaration, constants, mocks, constructor, and a `BuildSourceOrder` helper**

Create `backend/test/Anela.Heblo.Tests/Features/Manufacture/DuplicateManufactureOrderHandlerTests.cs` with this exact content:

```csharp
using Anela.Heblo.Application.Features.Manufacture.UseCases.DuplicateManufactureOrder;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class DuplicateManufactureOrderHandlerTests
{
    private const int SourceOrderId = 42;
    private const int PersistedOrderId = 1234;
    private const string GeneratedOrderNumber = "MO-2026-0042";
    private const string DisplayName = "Test User";
    private const string ResponsiblePerson = "Jane Foreman";

    private static readonly DateTimeOffset FixedNow =
        new(2026, 6, 8, 10, 0, 0, TimeSpan.Zero);

    private readonly Mock<IManufactureOrderRepository> _repositoryMock = new();
    private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();
    private readonly Mock<TimeProvider> _timeProviderMock = new();
    private readonly DuplicateManufactureOrderHandler _handler;

    public DuplicateManufactureOrderHandlerTests()
    {
        _timeProviderMock.Setup(x => x.GetUtcNow()).Returns(FixedNow);

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("test-user-id", DisplayName, "test@example.com", true));

        _handler = new DuplicateManufactureOrderHandler(
            _repositoryMock.Object,
            _currentUserServiceMock.Object,
            _timeProviderMock.Object);
    }

    private static ManufactureOrder BuildSourceOrder(bool includeSemiProduct)
    {
        var order = new ManufactureOrder
        {
            Id = SourceOrderId,
            OrderNumber = "MO-2025-9999",
            ResponsiblePerson = ResponsiblePerson,
            State = ManufactureOrderState.Completed,
            PlannedDate = new DateOnly(2025, 1, 1),
            CreatedByUser = "Original Author",
            StateChangedByUser = "Original Author",
        };

        if (includeSemiProduct)
        {
            order.SemiProduct = new ManufactureOrderSemiProduct
            {
                ProductCode = "SEMI-001",
                ProductName = "Source Semi Product",
                PlannedQuantity = 1000m,
                ActualQuantity = 950m, // intentionally distinct from planned
                BatchMultiplier = 1.5m,
                ExpirationMonths = 24,
                LotNumber = "OLD-LOT",
                ExpirationDate = new DateOnly(2027, 1, 31),
            };
        }

        order.Products.Add(new ManufactureOrderProduct
        {
            ProductCode = "PROD-A",
            ProductName = "Source Product A",
            SemiProductCode = "SEMI-001",
            PlannedQuantity = 100m,
            ActualQuantity = 90m, // intentionally distinct from planned
            LotNumber = "OLD-LOT",
            ExpirationDate = new DateOnly(2027, 1, 31),
        });

        order.Products.Add(new ManufactureOrderProduct
        {
            ProductCode = "PROD-B",
            ProductName = "Source Product B",
            SemiProductCode = "SEMI-001",
            PlannedQuantity = 200m,
            ActualQuantity = 180m,
            LotNumber = "OLD-LOT",
            ExpirationDate = new DateOnly(2027, 1, 31),
        });

        return order;
    }
}
```

- [ ] **Step 3: Build the test project to confirm the skeleton compiles**

Run from repo root:
```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```
Expected: `Build succeeded` with 0 errors. If errors mention missing types, re-check the `using` directives above — they match the namespaces used by the handler at `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/DuplicateManufactureOrder/DuplicateManufactureOrderHandler.cs:1-6` and the entities in `backend/src/Anela.Heblo.Domain/Features/Manufacture/`.

- [ ] **Step 4: Commit the skeleton**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Manufacture/DuplicateManufactureOrderHandlerTests.cs
git commit -m "test(manufacture): scaffold DuplicateManufactureOrderHandler test class"
```

---

### Task 2: Add the "source order not found" test (FR-2)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Manufacture/DuplicateManufactureOrderHandlerTests.cs` (append a `[Fact]` inside the class, before the closing `}`)

- [ ] **Step 1: Insert the test method just below the `BuildSourceOrder` helper, still inside the class**

Add the following method directly under `BuildSourceOrder` and above the final `}` of the class:

```csharp
    [Fact]
    public async Task Handle_ReturnsOrderNotFound_WhenSourceOrderDoesNotExist()
    {
        // Arrange
        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(SourceOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder?)null);

        var request = new DuplicateManufactureOrderRequest { SourceOrderId = SourceOrderId };

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.ErrorCode.Should().Be(ErrorCodes.OrderNotFound);
        response.Success.Should().BeFalse();

        _repositoryMock.Verify(
            x => x.GenerateOrderNumberAsync(It.IsAny<CancellationToken>()),
            Times.Never);
        _repositoryMock.Verify(
            x => x.AddOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
```

- [ ] **Step 2: Run only this test and verify it passes**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~DuplicateManufactureOrderHandlerTests.Handle_ReturnsOrderNotFound_WhenSourceOrderDoesNotExist"
```
Expected: `Passed!  - Failed: 0, Passed: 1, Skipped: 0`.

If it fails on `response.Success.Should().BeFalse()`, open `backend/src/Anela.Heblo.Application/Shared/BaseResponse.cs` and confirm `Success` is computed from `ErrorCode == null`. If `BaseResponse` no longer exposes a boolean `Success`, drop that single line and re-run; the `ErrorCode` and `Verify(...Times.Never)` assertions are the load-bearing ones.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Manufacture/DuplicateManufactureOrderHandlerTests.cs
git commit -m "test(manufacture): cover DuplicateManufactureOrderHandler not-found branch"
```

---

### Task 3: Add the "full duplication with SemiProduct" test (FR-3)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Manufacture/DuplicateManufactureOrderHandlerTests.cs` (append a `[Fact]` after the Task 2 test)

- [ ] **Step 1: Insert the test method directly below the Task 2 test, still inside the class**

Add the following method:

```csharp
    [Fact]
    public async Task Handle_DuplicatesAllFields_WhenSourceHasSemiProductAndProducts()
    {
        // Arrange
        var sourceOrder = BuildSourceOrder(includeSemiProduct: true);

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(SourceOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceOrder);
        _repositoryMock
            .Setup(x => x.GenerateOrderNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(GeneratedOrderNumber);

        ManufactureOrder? captured = null;
        _repositoryMock
            .Setup(x => x.AddOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .Callback<ManufactureOrder, CancellationToken>((order, _) => captured = order)
            .ReturnsAsync((ManufactureOrder order, CancellationToken _) =>
            {
                order.Id = PersistedOrderId;
                return order;
            });

        var request = new DuplicateManufactureOrderRequest { SourceOrderId = SourceOrderId };

        var expectedLot = ManufactureOrderExtensions.GetDefaultLot(FixedNow.UtcDateTime);
        var expectedExpiration = ManufactureOrderExtensions.GetDefaultExpiration(
            FixedNow.UtcDateTime,
            sourceOrder.SemiProduct!.ExpirationMonths);
        var expectedPlannedDate = DateOnly.FromDateTime(FixedNow.UtcDateTime);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert response
        response.Should().NotBeNull();
        response.ErrorCode.Should().BeNull();
        response.Id.Should().Be(PersistedOrderId);
        response.OrderNumber.Should().Be(GeneratedOrderNumber);

        // Assert captured order
        captured.Should().NotBeNull();
        captured!.OrderNumber.Should().Be(GeneratedOrderNumber);
        captured.State.Should().Be(ManufactureOrderState.Draft);
        captured.CreatedByUser.Should().Be(DisplayName);
        captured.StateChangedByUser.Should().Be(DisplayName);
        captured.ResponsiblePerson.Should().Be(ResponsiblePerson);
        captured.PlannedDate.Should().Be(expectedPlannedDate);

        // Assert duplicated semi-product
        captured.SemiProduct.Should().NotBeNull();
        captured.SemiProduct!.ProductCode.Should().Be(sourceOrder.SemiProduct.ProductCode);
        captured.SemiProduct.ProductName.Should().Be(sourceOrder.SemiProduct.ProductName);
        captured.SemiProduct.PlannedQuantity.Should().Be(sourceOrder.SemiProduct.PlannedQuantity);
        captured.SemiProduct.ActualQuantity.Should().Be(sourceOrder.SemiProduct.PlannedQuantity);
        captured.SemiProduct.BatchMultiplier.Should().Be(sourceOrder.SemiProduct.BatchMultiplier);
        captured.SemiProduct.ExpirationMonths.Should().Be(sourceOrder.SemiProduct.ExpirationMonths);
        captured.SemiProduct.LotNumber.Should().Be(expectedLot);
        captured.SemiProduct.ExpirationDate.Should().Be(expectedExpiration);

        // Assert duplicated products (collection-shaped, order-preserving)
        captured.Products.Should().HaveCount(sourceOrder.Products.Count);
        for (var i = 0; i < sourceOrder.Products.Count; i++)
        {
            var src = sourceOrder.Products[i];
            var dup = captured.Products[i];

            dup.ProductCode.Should().Be(src.ProductCode);
            dup.ProductName.Should().Be(src.ProductName);
            dup.SemiProductCode.Should().Be(src.SemiProductCode);
            dup.PlannedQuantity.Should().Be(src.PlannedQuantity);
            dup.ActualQuantity.Should().Be(src.PlannedQuantity);
            dup.LotNumber.Should().Be(expectedLot);
            dup.ExpirationDate.Should().Be(expectedExpiration);
        }
    }
```

- [ ] **Step 2: Run only this test and verify it passes**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~DuplicateManufactureOrderHandlerTests.Handle_DuplicatesAllFields_WhenSourceHasSemiProductAndProducts"
```
Expected: `Passed!  - Failed: 0, Passed: 1, Skipped: 0`.

If `Mock<TimeProvider>` fails to set up `GetUtcNow()` with a "non-overridable" error, fall back to the same workaround used in `backend/test/Anela.Heblo.Tests/Features/Manufacture/CreateManufactureOrderHandlerTests.cs:47` — pass `TimeProvider.System` and instead change the assertion for `PlannedDate` to use `DateOnly.FromDateTime(DateTime.UtcNow)` with a tolerance comparison. Do this only if the mock genuinely refuses; the sibling tests confirm it works on net8.0, so it should not be needed.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Manufacture/DuplicateManufactureOrderHandlerTests.cs
git commit -m "test(manufacture): cover DuplicateManufactureOrderHandler happy path with semi-product"
```

---

### Task 4: Add the "no SemiProduct" test (FR-4)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Manufacture/DuplicateManufactureOrderHandlerTests.cs` (append a `[Fact]` after the Task 3 test)

- [ ] **Step 1: Insert the test method directly below the Task 3 test, still inside the class**

Add the following method:

```csharp
    [Fact]
    public async Task Handle_OmitsSemiProductAndLeavesProductExpirationNull_WhenSourceHasNoSemiProduct()
    {
        // Arrange
        var sourceOrder = BuildSourceOrder(includeSemiProduct: false);

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(SourceOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceOrder);
        _repositoryMock
            .Setup(x => x.GenerateOrderNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(GeneratedOrderNumber);

        ManufactureOrder? captured = null;
        _repositoryMock
            .Setup(x => x.AddOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .Callback<ManufactureOrder, CancellationToken>((order, _) => captured = order)
            .ReturnsAsync((ManufactureOrder order, CancellationToken _) =>
            {
                order.Id = PersistedOrderId;
                return order;
            });

        var request = new DuplicateManufactureOrderRequest { SourceOrderId = SourceOrderId };

        var expectedLot = ManufactureOrderExtensions.GetDefaultLot(FixedNow.UtcDateTime);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert response
        response.Should().NotBeNull();
        response.ErrorCode.Should().BeNull();
        response.Id.Should().Be(PersistedOrderId);
        response.OrderNumber.Should().Be(GeneratedOrderNumber);

        // Assert captured order — no semi-product attached
        captured.Should().NotBeNull();
        captured!.SemiProduct.Should().BeNull();

        // Assert products are duplicated and their expiration is null
        captured.Products.Should().HaveCount(sourceOrder.Products.Count);
        for (var i = 0; i < sourceOrder.Products.Count; i++)
        {
            var src = sourceOrder.Products[i];
            var dup = captured.Products[i];

            dup.ProductCode.Should().Be(src.ProductCode);
            dup.ProductName.Should().Be(src.ProductName);
            dup.SemiProductCode.Should().Be(src.SemiProductCode);
            dup.PlannedQuantity.Should().Be(src.PlannedQuantity);
            dup.ActualQuantity.Should().Be(src.PlannedQuantity);
            dup.LotNumber.Should().Be(expectedLot);
            dup.ExpirationDate.Should().BeNull();
        }
    }
```

- [ ] **Step 2: Run only this test and verify it passes**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~DuplicateManufactureOrderHandlerTests.Handle_OmitsSemiProductAndLeavesProductExpirationNull_WhenSourceHasNoSemiProduct"
```
Expected: `Passed!  - Failed: 0, Passed: 1, Skipped: 0`.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Manufacture/DuplicateManufactureOrderHandlerTests.cs
git commit -m "test(manufacture): cover DuplicateManufactureOrderHandler branch without semi-product"
```

---

### Task 5: Final verification — full Manufacture-area test run, build, format

**Files:**
- None (verification only)

- [ ] **Step 1: Run the entire `DuplicateManufactureOrderHandlerTests` class**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~DuplicateManufactureOrderHandlerTests"
```
Expected: `Passed!  - Failed: 0, Passed: 3, Skipped: 0`. If fewer than 3, a `[Fact]` was lost — re-read the file at the class level.

- [ ] **Step 2: Run all Manufacture-feature tests to confirm no neighbour regressed**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Manufacture"
```
Expected: all tests pass. The brief stated 64 Manufacture tests; this run should report **67 passed** (64 prior + 3 new). If the prior count differs, that is fine — the load-bearing check is `Failed: 0` and a count that is 3 higher than the pre-change baseline. If anything fails that did not touch this file, do not edit unrelated tests — stop and report.

- [ ] **Step 3: Build the whole solution**

```bash
dotnet build
```
Expected: `Build succeeded`, 0 errors.

- [ ] **Step 4: Run `dotnet format` and confirm no diff**

```bash
dotnet format backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
git diff --stat
```
Expected: `git diff --stat` reports no changes to `DuplicateManufactureOrderHandlerTests.cs`. If `dotnet format` rewrites the file, `git add` and amend the most recent Task 4 commit:
```bash
git add backend/test/Anela.Heblo.Tests/Features/Manufacture/DuplicateManufactureOrderHandlerTests.cs
git commit --amend --no-edit
```

- [ ] **Step 5: Confirm no production code was touched**

```bash
git diff main...HEAD --stat -- backend/src
```
Expected: empty output (no `backend/src/...` files changed since `main`). If anything appears, that file must be reverted — this plan adds tests only.

---

## Self-Review

**Spec coverage:**
- FR-1 (location/naming) → Task 1 step 2 places the file at the flat path mandated by the architecture review's amendment #1, uses `public class` per amendment #2, and uses xUnit `[Fact]` + Moq + FluentAssertions per amendment #4.
- FR-2 (not found) → Task 2, with explicit `Verify(...Times.Never)` for `GenerateOrderNumberAsync` and `AddOrderAsync`.
- FR-3 (full duplication) → Task 3 covers every bullet: order number, `Draft`, current-user attribution, `PlannedDate`, semi-product fields including `ActualQuantity` reset, lot and expiration via the static helpers, and per-product assertions.
- FR-4 (no semi-product) → Task 4 covers `SemiProduct == null`, product count, `ActualQuantity` reset, null `ExpirationDate`, and `LotNumber` from the helper.
- FR-5 (no regressions) → Task 5 steps 2, 3, 5.
- NFR-1 (perf) → Three pure in-process unit tests with mocked I/O; trivially satisfies the 100 ms / 1 s budgets.
- NFR-2 (security) → Task 5 step 5 enforces "no production code touched".
- NFR-3 (maintainability) → AAA structure, behavior-named tests, constants at top, single `BuildSourceOrder` helper under the 30-line cap.
- NFR-4 (determinism) → `Mock<TimeProvider>` returning `FixedNow`; lot/expiration expected values computed by calling the same static helpers as the handler (no formatted-string literals).

**Architecture-review amendments honored:** flat path (#1), `public class` not `sealed` (#2), `Mock<TimeProvider>` instead of `Microsoft.Extensions.Time.Testing` (#3 — no csproj edit), Moq only (#4), `DateOnly.FromDateTime(FixedNow.UtcDateTime)` for `PlannedDate` (#5).

**Placeholder scan:** No "TBD", no "implement later", no "similar to Task N", no "add appropriate error handling". Each step has either a complete code block or an exact command with an exact expected output.

**Type consistency:** `IManufactureOrderRepository`, `ICurrentUserService`, `TimeProvider`, `CurrentUser(string?, string?, string?, bool)`, `DuplicateManufactureOrderRequest.SourceOrderId`, `DuplicateManufactureOrderResponse.{Id, OrderNumber, ErrorCode, Success}`, `ManufactureOrder.{OrderNumber, State, CreatedByUser, StateChangedByUser, ResponsiblePerson, PlannedDate, SemiProduct, Products}`, `ManufactureOrderSemiProduct.{ProductCode, ProductName, PlannedQuantity, ActualQuantity, BatchMultiplier, ExpirationMonths, LotNumber, ExpirationDate}`, `ManufactureOrderProduct.{ProductCode, ProductName, SemiProductCode, PlannedQuantity, ActualQuantity, LotNumber, ExpirationDate}`, `ManufactureOrderExtensions.GetDefaultLot(DateTime)`, `ManufactureOrderExtensions.GetDefaultExpiration(DateTime, int)`, `ErrorCodes.OrderNotFound`, `ManufactureOrderState.{Draft, Completed}`. All cross-referenced against the actual source files. Consistent across all tasks.

**Out-of-scope items explicitly NOT covered (per spec):** no handler edits, no integration tests, no controller tests, no coverage of other Manufacture handlers, no frontend/E2E work, no assertion on `CreatedDate` / `StateChangedAt` (they are produced from `DateTime.UtcNow` inside the handler and are out of NFR-4's determinism guarantee — adding them would be flaky).
