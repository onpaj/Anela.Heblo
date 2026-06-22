# ResolveManualActionHandler Test Coverage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Write 12 unit tests for `ResolveManualActionHandler` to bring line coverage from 18% to ≥85%.

**Architecture:** Single new test file following the `SubmitManufactureHandlerTests.cs` constructor pattern — field-level Mock initialisation, handler built in the constructor, private `BuildOrder()` / `BuildRequest()` helpers, no new dependencies.

**Tech Stack:** .NET 8, C#, xUnit 2.9.2, Moq 4.20.72, FluentAssertions 6.12.0

---

### task: write-handler-tests

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Manufacture/ResolveManualActionHandlerTests.cs`

#### Background — handler under test

`ResolveManualActionHandler` lives at:
`backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/ResolveManualAction/ResolveManualActionHandler.cs`

Constructor:
```csharp
public ResolveManualActionHandler(
    IManufactureOrderRepository repository,
    ICurrentUserService currentUserService,
    ILogger<ResolveManualActionHandler> logger)
```

The `Handle` method:
1. Calls `_repository.GetOrderByIdAsync(request.OrderId, cancellationToken)` — returns `null` → early-out with `ErrorCodes.ResourceNotFound`.
2. Calls `_currentUserService.GetCurrentUser()` — may return `null`.
3. Conditionally sets `ErpOrderNumberSemiproduct`, `ErpOrderNumberProduct`, `ErpDiscardResidueDocumentNumber` (+ date) on the order.
4. Always sets `order.ManualActionRequired = false`.
5. Conditionally adds a `ManufactureOrderNote` to `order.Notes`.
6. Calls `_repository.UpdateOrderAsync(order, cancellationToken)`.
7. Returns `new ResolveManualActionResponse()` on success.
8. Catches any `Exception` and returns `ErrorCodes.InternalServerError`.

Key types (namespaces shown in the using directives below):
- `IManufactureOrderRepository` — `Anela.Heblo.Domain.Features.Manufacture`
- `ICurrentUserService` — `Anela.Heblo.Domain.Features.Users`
- `CurrentUser` record — `CurrentUser(string? Id, string? Name, string? Email, bool IsAuthenticated)`
- `ManufactureOrder` — `Anela.Heblo.Domain.Features.Manufacture` — has `List<ManufactureOrderNote> Notes { get; set; } = new()`, `ManualActionRequired`, `ErpOrderNumberSemiproduct`, `ErpOrderNumberProduct`, `ErpDiscardResidueDocumentNumber`, `ErpDiscardResidueDocumentNumberDate`
- `ManufactureOrderNote` — same namespace — `Text`, `CreatedAt`, `CreatedByUser`
- `ResolveManualActionRequest` / `ResolveManualActionResponse` — `Anela.Heblo.Application.Features.Manufacture.UseCases.ResolveManualAction`
- `ErrorCodes` — `Anela.Heblo.Application.Shared`
- `BaseResponse` (parent of `ResolveManualActionResponse`) — has `bool Success`, `ErrorCodes? ErrorCode`

`ManufactureOrder` has two non-nullable `string` fields that must be set to avoid null-reference issues at DB layer — they are irrelevant to this handler's logic but must not be left `null!` when constructing test objects: `OrderNumber` and `StateChangedByUser`.

`UpdateOrderAsync` signature: `Task<ManufactureOrder> UpdateOrderAsync(ManufactureOrder order, CancellationToken cancellationToken = default)` — the tests that reach this call must set up a mock return value.

---

- [ ] **Step 1: Create the test file with the complete test class**

Create `backend/test/Anela.Heblo.Tests/Features/Manufacture/ResolveManualActionHandlerTests.cs` with the following content:

```csharp
using Anela.Heblo.Application.Features.Manufacture.UseCases.ResolveManualAction;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class ResolveManualActionHandlerTests
{
    private readonly Mock<IManufactureOrderRepository> _repositoryMock = new();
    private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();
    private readonly Mock<ILogger<ResolveManualActionHandler>> _loggerMock = new();
    private readonly ResolveManualActionHandler _handler;

    public ResolveManualActionHandlerTests()
    {
        _handler = new ResolveManualActionHandler(
            _repositoryMock.Object,
            _currentUserServiceMock.Object,
            _loggerMock.Object);
    }

    // ── 1. Order not found ────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_WhenOrderNotFound_ReturnsResourceNotFoundError()
    {
        _repositoryMock
            .Setup(r => r.GetOrderByIdAsync(99, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder?)null);

        var result = await _handler.Handle(
            new ResolveManualActionRequest { OrderId = 99 },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
        _repositoryMock.Verify(
            r => r.UpdateOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── 2. ManualActionRequired is always reset ───────────────────────────────

    [Fact]
    public async Task Handle_WhenOrderFound_ResetsManualActionRequired()
    {
        var order = BuildOrder(manualActionRequired: true);
        SetupFoundOrder(order);
        SetupCurrentUser("Test User");

        var result = await _handler.Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        order.ManualActionRequired.Should().BeFalse();
    }

    // ── 3 & 4. ErpOrderNumberSemiproduct ─────────────────────────────────────

    [Fact]
    public async Task Handle_WhenSemiproductNumberProvided_UpdatesField()
    {
        var order = BuildOrder();
        SetupFoundOrder(order);
        SetupCurrentUser("Test User");

        await _handler.Handle(
            BuildRequest(erpOrderNumberSemiproduct: "SEMI-001"),
            CancellationToken.None);

        order.ErpOrderNumberSemiproduct.Should().Be("SEMI-001");
    }

    [Fact]
    public async Task Handle_WhenSemiproductNumberOmitted_DoesNotOverwriteField()
    {
        var order = BuildOrder();
        order.ErpOrderNumberSemiproduct = "EXISTING-SEMI";
        SetupFoundOrder(order);
        SetupCurrentUser("Test User");

        await _handler.Handle(
            BuildRequest(erpOrderNumberSemiproduct: null),
            CancellationToken.None);

        order.ErpOrderNumberSemiproduct.Should().Be("EXISTING-SEMI");
    }

    // ── 5 & 6. ErpOrderNumberProduct ─────────────────────────────────────────

    [Fact]
    public async Task Handle_WhenProductNumberProvided_UpdatesField()
    {
        var order = BuildOrder();
        SetupFoundOrder(order);
        SetupCurrentUser("Test User");

        await _handler.Handle(
            BuildRequest(erpOrderNumberProduct: "PROD-001"),
            CancellationToken.None);

        order.ErpOrderNumberProduct.Should().Be("PROD-001");
    }

    [Fact]
    public async Task Handle_WhenProductNumberOmitted_DoesNotOverwriteField()
    {
        var order = BuildOrder();
        order.ErpOrderNumberProduct = "EXISTING-PROD";
        SetupFoundOrder(order);
        SetupCurrentUser("Test User");

        await _handler.Handle(
            BuildRequest(erpOrderNumberProduct: null),
            CancellationToken.None);

        order.ErpOrderNumberProduct.Should().Be("EXISTING-PROD");
    }

    // ── 7 & 8. ErpDiscardResidueDocumentNumber ────────────────────────────────

    [Fact]
    public async Task Handle_WhenDiscardDocumentProvided_UpdatesFieldAndTimestamp()
    {
        var order = BuildOrder();
        SetupFoundOrder(order);
        SetupCurrentUser("Test User");
        var before = DateTime.UtcNow;

        await _handler.Handle(
            BuildRequest(erpDiscardResidueDocumentNumber: "DISCARD-001"),
            CancellationToken.None);

        order.ErpDiscardResidueDocumentNumber.Should().Be("DISCARD-001");
        order.ErpDiscardResidueDocumentNumberDate.Should().NotBeNull();
        order.ErpDiscardResidueDocumentNumberDate!.Value
            .Should().BeCloseTo(before, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Handle_WhenDiscardDocumentOmitted_DoesNotSetTimestamp()
    {
        var order = BuildOrder();
        SetupFoundOrder(order);
        SetupCurrentUser("Test User");

        await _handler.Handle(
            BuildRequest(erpDiscardResidueDocumentNumber: null),
            CancellationToken.None);

        order.ErpDiscardResidueDocumentNumberDate.Should().BeNull();
    }

    // ── 9. Note with authenticated user ──────────────────────────────────────

    [Fact]
    public async Task Handle_WhenNoteProvidedAndUserPresent_AddsNoteWithUserName()
    {
        var order = BuildOrder();
        SetupFoundOrder(order);
        SetupCurrentUser("Ondra Novak");

        await _handler.Handle(
            BuildRequest(note: "Please check the batch."),
            CancellationToken.None);

        order.Notes.Should().HaveCount(1);
        order.Notes[0].Text.Should().Be("Please check the batch.");
        order.Notes[0].CreatedByUser.Should().Be("Ondra Novak");
    }

    // ── 10. Note when current user is null ───────────────────────────────────

    [Fact]
    public async Task Handle_WhenNoteProvidedAndUserNull_AddsNoteWithUnknownUser()
    {
        var order = BuildOrder();
        SetupFoundOrder(order);
        _currentUserServiceMock
            .Setup(s => s.GetCurrentUser())
            .Returns((CurrentUser?)null!);

        await _handler.Handle(
            BuildRequest(note: "Fallback note."),
            CancellationToken.None);

        order.Notes.Should().HaveCount(1);
        order.Notes[0].Text.Should().Be("Fallback note.");
        order.Notes[0].CreatedByUser.Should().Be("Unknown User");
    }

    // ── 11. No note when Note field is omitted ────────────────────────────────

    [Fact]
    public async Task Handle_WhenNoteOmitted_DoesNotAddNote()
    {
        var order = BuildOrder();
        SetupFoundOrder(order);
        SetupCurrentUser("Test User");

        await _handler.Handle(
            BuildRequest(note: null),
            CancellationToken.None);

        order.Notes.Should().BeEmpty();
    }

    // ── 12. All fields provided — full happy path ─────────────────────────────

    [Fact]
    public async Task Handle_WithAllFieldsProvided_ReturnsSuccessAndUpdatesAllFields()
    {
        var order = BuildOrder(manualActionRequired: true);
        SetupFoundOrder(order);
        SetupCurrentUser("Ondra Novak");

        var result = await _handler.Handle(
            new ResolveManualActionRequest
            {
                OrderId = 1,
                ErpOrderNumberSemiproduct = "SEMI-ALL",
                ErpOrderNumberProduct = "PROD-ALL",
                ErpDiscardResidueDocumentNumber = "DISCARD-ALL",
                Note = "All fields set."
            },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        order.ManualActionRequired.Should().BeFalse();
        order.ErpOrderNumberSemiproduct.Should().Be("SEMI-ALL");
        order.ErpOrderNumberProduct.Should().Be("PROD-ALL");
        order.ErpDiscardResidueDocumentNumber.Should().Be("DISCARD-ALL");
        order.ErpDiscardResidueDocumentNumberDate.Should().NotBeNull();
        order.Notes.Should().HaveCount(1);
        order.Notes[0].CreatedByUser.Should().Be("Ondra Novak");
        _repositoryMock.Verify(
            r => r.UpdateOrderAsync(order, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ManufactureOrder BuildOrder(bool manualActionRequired = false) =>
        new()
        {
            Id = 1,
            OrderNumber = "MO-2026-001",
            StateChangedByUser = "system",
            ManualActionRequired = manualActionRequired
        };

    private static ResolveManualActionRequest BuildRequest(
        string? erpOrderNumberSemiproduct = null,
        string? erpOrderNumberProduct = null,
        string? erpDiscardResidueDocumentNumber = null,
        string? note = null) =>
        new()
        {
            OrderId = 1,
            ErpOrderNumberSemiproduct = erpOrderNumberSemiproduct,
            ErpOrderNumberProduct = erpOrderNumberProduct,
            ErpDiscardResidueDocumentNumber = erpDiscardResidueDocumentNumber,
            Note = note
        };

    private void SetupFoundOrder(ManufactureOrder order)
    {
        _repositoryMock
            .Setup(r => r.GetOrderByIdAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        _repositoryMock
            .Setup(r => r.UpdateOrderAsync(order, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
    }

    private void SetupCurrentUser(string name)
    {
        _currentUserServiceMock
            .Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser(Id: "u1", Name: name, Email: "test@anela.cz", IsAuthenticated: true));
    }
}
```

- [ ] **Step 2: Build to verify the file compiles**

Run from the repo root:
```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected output ends with:
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

If you get `CS0246` (type not found), check the using directives — the three main namespaces to verify are:
- `Anela.Heblo.Application.Features.Manufacture.UseCases.ResolveManualAction` (for Request/Response/Handler)
- `Anela.Heblo.Domain.Features.Manufacture` (for ManufactureOrder, IManufactureOrderRepository)
- `Anela.Heblo.Domain.Features.Users` (for CurrentUser, ICurrentUserService)

- [ ] **Step 3: Run the 12 new tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ResolveManualActionHandlerTests" \
  --verbosity normal
```

Expected: all 12 tests pass.

```
Passed!  - Failed: 0, Passed: 12, Skipped: 0
```

If any test fails, read the failure message carefully:
- `NullReferenceException` inside `GetCurrentUser()` → the mock for `ICurrentUserService` is missing a setup — check `SetupCurrentUser()` is called in that test or that the null-return test uses the correct mock setup.
- `UpdateOrderAsync` `ReturnsAsync` missing → make sure `SetupFoundOrder()` is called in that test — it sets up both `GetOrderByIdAsync` and `UpdateOrderAsync`.
- Wrong `CreatedByUser` value → the handler uses `currentUser?.Name ?? "Unknown User"`, so if your mock returns a `CurrentUser` with `Name: null`, the fallback will fire; adjust the `CurrentUser` constructor arguments.

- [ ] **Step 4: Run the full Manufacture test suite to check for regressions**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Manufacture" \
  --verbosity minimal
```

Expected: all previously-passing tests continue to pass.

- [ ] **Step 5: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Manufacture/ResolveManualActionHandlerTests.cs
git commit -m "test: add unit tests for ResolveManualActionHandler (coverage 18% → ≥85%)"
```
