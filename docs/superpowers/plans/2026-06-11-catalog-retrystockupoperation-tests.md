# RetryStockUpOperationHandler Test Coverage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a unit-test class that pins down all four behavior branches of `RetryStockUpOperationHandler` (not-found, already-completed, Failed→Reset, non-Completed→ForceReset) so a future regression in branch selection or `Reset`/`ForceReset` choice fails the build.

**Architecture:** Single new xUnit test file in `backend/test/Anela.Heblo.Tests/Features/Catalog/`. Real `StockUpOperation` entity (state arranged via public `MarkAs*` methods); Moq for `IStockUpOperationRepository` and `ILogger<RetryStockUpOperationHandler>`. The `Reset` vs `ForceReset` branch is distinguished by asserting on `LogLevel.Warning` count — the handler emits exactly one Warning on the ForceReset branch and zero on the Reset branch.

**Tech Stack:** xUnit, Moq, `Microsoft.Extensions.Logging.Abstractions`, plain `Assert.*` (matches sibling `AcceptStockUpOperationHandlerTests.cs` — do NOT switch to FluentAssertions even though it appears in global rules; project convention here uses `Assert.*`).

---

## File Structure

Single new file. No project file edits — test discovery is convention-based.

```
backend/test/Anela.Heblo.Tests/Features/Catalog/
└── RetryStockUpOperationHandlerTests.cs   ← NEW (5 [Fact] methods)
```

Reference for style (read first if unfamiliar): `backend/test/Anela.Heblo.Tests/Features/Catalog/AcceptStockUpOperationHandlerTests.cs`.

---

## Pre-flight: Verify the handler under test

Before writing tests, open `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/RetryStockUpOperation/RetryStockUpOperationHandler.cs` and confirm:

1. Not-found error string is exactly: `$"Operation with ID {request.OperationId} not found"`.
2. Already-completed error string is exactly: `$"Operation {operation.DocumentNumber} is already completed and cannot be retried"`.
3. The branch order is: `if (operation.State == StockUpOperationState.Failed) operation.Reset(); else { _logger.LogWarning(...); operation.ForceReset(); }`.

If any of these differ, **stop and update the test arrange/assert strings to match the production code** — do not modify the handler.

Also confirm the domain entity at `backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/StockUpOperation.cs` exposes:
- `public StockUpOperation(string documentNumber, string productCode, int amount, StockUpSourceType sourceType, int sourceId)` (constructor leaves state as `Pending`).
- `public void MarkAsSubmitted(DateTime timestamp)` (requires current state `Pending`).
- `public void MarkAsCompleted(DateTime timestamp)` (requires current state `Submitted` or `Pending`).
- `public void MarkAsFailed(DateTime timestamp, string errorMessage)` (no state guard; non-empty `errorMessage` required).

---

## Task 1: Create the test class skeleton

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/RetryStockUpOperationHandlerTests.cs`

- [ ] **Step 1: Create the file with using directives, namespace, class, and constructor**

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Application.Features.Catalog.UseCases.RetryStockUpOperation;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class RetryStockUpOperationHandlerTests
{
    private readonly Mock<IStockUpOperationRepository> _repositoryMock;
    private readonly Mock<ILogger<RetryStockUpOperationHandler>> _loggerMock;
    private readonly RetryStockUpOperationHandler _handler;
    private static readonly DateTime FixedNow = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public RetryStockUpOperationHandlerTests()
    {
        _repositoryMock = new Mock<IStockUpOperationRepository>();
        _loggerMock = new Mock<ILogger<RetryStockUpOperationHandler>>();
        _handler = new RetryStockUpOperationHandler(
            _repositoryMock.Object,
            _loggerMock.Object);
    }
}
```

- [ ] **Step 2: Verify the file compiles**

Run: `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: Build succeeded, 0 errors, 0 warnings.

If a warning appears (nullable, unused using, etc.), fix it before continuing.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/RetryStockUpOperationHandlerTests.cs
git commit -m "test(catalog): add RetryStockUpOperationHandlerTests skeleton"
```

---

## Task 2: FR-1 — operation not found returns failure

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/RetryStockUpOperationHandlerTests.cs`

- [ ] **Step 1: Add the [Fact] method inside the class (after the constructor)**

```csharp
    [Fact]
    public async Task Handle_WhenOperationNotFound_ReturnsFailure()
    {
        // Arrange
        var request = new RetryStockUpOperationRequest { OperationId = 999 };

        _repositoryMock
            .Setup(r => r.GetByIdAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockUpOperation?)null);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.False(response.Success);
        Assert.Equal(StockUpResultStatus.Failed, response.Status);
        Assert.Contains("999", response.ErrorMessage);

        _repositoryMock.Verify(
            r => r.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }
```

- [ ] **Step 2: Run the test and verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RetryStockUpOperationHandlerTests.Handle_WhenOperationNotFound_ReturnsFailure"`
Expected: `Passed: 1`. If the test fails, the production not-found message has drifted — update `Assert.Contains` to match the current handler text, not the other way around.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/RetryStockUpOperationHandlerTests.cs
git commit -m "test(catalog): RetryStockUpOperationHandler returns failure when not found"
```

---

## Task 3: FR-2 — already-completed operation is rejected

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/RetryStockUpOperationHandlerTests.cs`

- [ ] **Step 1: Add the [Fact] method**

```csharp
    [Fact]
    public async Task Handle_WhenOperationIsCompleted_ReturnsAlreadyCompleted()
    {
        // Arrange
        var operation = new StockUpOperation(
            "DOC-001",
            "PROD-001",
            10,
            StockUpSourceType.TransportBox,
            1);
        operation.MarkAsCompleted(FixedNow); // Pending -> Completed (allowed by domain)

        var request = new RetryStockUpOperationRequest { OperationId = 1 };

        _repositoryMock
            .Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(operation);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.False(response.Success);
        Assert.Equal(StockUpResultStatus.AlreadyCompleted, response.Status);
        Assert.Contains("DOC-001", response.ErrorMessage);
        Assert.Equal(StockUpOperationState.Completed, operation.State);

        _repositoryMock.Verify(
            r => r.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }
```

- [ ] **Step 2: Run the test and verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RetryStockUpOperationHandlerTests.Handle_WhenOperationIsCompleted_ReturnsAlreadyCompleted"`
Expected: `Passed: 1`.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/RetryStockUpOperationHandlerTests.cs
git commit -m "test(catalog): RetryStockUpOperationHandler rejects completed operations"
```

---

## Task 4: FR-3 — Failed operation routes through `Reset()` (no Warning)

This test pins down the critical branch decision: a `Failed` operation must go through `Reset()`, which is observable by the **absence** of any `LogLevel.Warning` entry.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/RetryStockUpOperationHandlerTests.cs`

- [ ] **Step 1: Add the [Fact] method**

```csharp
    [Fact]
    public async Task Handle_WhenOperationIsFailed_CallsResetAndReturnsInProgress()
    {
        // Arrange
        var operation = new StockUpOperation(
            "DOC-001",
            "PROD-001",
            10,
            StockUpSourceType.TransportBox,
            1);
        operation.MarkAsFailed(FixedNow, "some error");

        var request = new RetryStockUpOperationRequest { OperationId = 1 };

        _repositoryMock
            .Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(operation);

        _repositoryMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Equal(StockUpResultStatus.InProgress, response.Status);
        Assert.Null(response.ErrorMessage);

        // Observable Reset() post-state
        Assert.Equal(StockUpOperationState.Pending, operation.State);
        Assert.Null(operation.SubmittedAt);
        Assert.Null(operation.CompletedAt);
        Assert.Null(operation.ErrorMessage);

        _repositoryMock.Verify(
            r => r.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);

        // Branch selection signal: handler emits NO Warning on the Reset branch.
        // If this assertion ever fails, the handler has been switched to ForceReset
        // (or someone introduced a new Warning above this call site).
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }
```

- [ ] **Step 2: Run the test and verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RetryStockUpOperationHandlerTests.Handle_WhenOperationIsFailed_CallsResetAndReturnsInProgress"`
Expected: `Passed: 1`.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/RetryStockUpOperationHandlerTests.cs
git commit -m "test(catalog): RetryStockUpOperationHandler routes Failed through Reset"
```

---

## Task 5: FR-4 — Submitted operation routes through `ForceReset()` (Warning emitted)

This test pins down the second side of the branch: a `Submitted` (stuck) operation must go through `ForceReset()`, observable by **exactly one** `LogLevel.Warning` entry.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/RetryStockUpOperationHandlerTests.cs`

- [ ] **Step 1: Add the [Fact] method**

```csharp
    [Fact]
    public async Task Handle_WhenOperationIsSubmitted_CallsForceResetAndReturnsInProgress()
    {
        // Arrange
        var operation = new StockUpOperation(
            "DOC-001",
            "PROD-001",
            10,
            StockUpSourceType.TransportBox,
            1);
        operation.MarkAsSubmitted(FixedNow);

        var request = new RetryStockUpOperationRequest { OperationId = 1 };

        _repositoryMock
            .Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(operation);

        _repositoryMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Equal(StockUpResultStatus.InProgress, response.Status);
        Assert.Null(response.ErrorMessage);

        // Observable ForceReset() post-state
        Assert.Equal(StockUpOperationState.Pending, operation.State);
        Assert.Null(operation.SubmittedAt);
        Assert.Null(operation.CompletedAt);

        _repositoryMock.Verify(
            r => r.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);

        // Branch selection signal: the handler emits exactly one Warning
        // immediately before calling ForceReset(). If this drops to Times.Never,
        // the branch swap regression has landed.
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
```

- [ ] **Step 2: Run the test and verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RetryStockUpOperationHandlerTests.Handle_WhenOperationIsSubmitted_CallsForceResetAndReturnsInProgress"`
Expected: `Passed: 1`.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/RetryStockUpOperationHandlerTests.cs
git commit -m "test(catalog): RetryStockUpOperationHandler routes Submitted through ForceReset"
```

---

## Task 6: FR-5 — Pending (stuck) operation also routes through `ForceReset()`

Parametrizing the stuck-state branch over a second input (`Pending`) catches an off-by-one branch error like `if (state == Submitted)` (instead of the current `if (state == Failed)`).

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/RetryStockUpOperationHandlerTests.cs`

- [ ] **Step 1: Add the [Fact] method**

```csharp
    [Fact]
    public async Task Handle_WhenOperationIsPending_CallsForceResetAndReturnsInProgress()
    {
        // Arrange — constructor leaves the operation in Pending state; no Mark* call needed.
        var operation = new StockUpOperation(
            "DOC-001",
            "PROD-001",
            10,
            StockUpSourceType.TransportBox,
            1);

        var request = new RetryStockUpOperationRequest { OperationId = 1 };

        _repositoryMock
            .Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(operation);

        _repositoryMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Equal(StockUpResultStatus.InProgress, response.Status);
        Assert.Null(response.ErrorMessage);

        // State remains Pending after ForceReset (Pending -> Pending is valid).
        Assert.Equal(StockUpOperationState.Pending, operation.State);
        Assert.Null(operation.SubmittedAt); // Pending operations never had SubmittedAt; harmless redundancy.
        Assert.Null(operation.CompletedAt);

        _repositoryMock.Verify(
            r => r.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);

        // Branch selection signal — same proxy as the Submitted case.
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
```

- [ ] **Step 2: Run the test and verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RetryStockUpOperationHandlerTests.Handle_WhenOperationIsPending_CallsForceResetAndReturnsInProgress"`
Expected: `Passed: 1`.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/RetryStockUpOperationHandlerTests.cs
git commit -m "test(catalog): RetryStockUpOperationHandler routes Pending through ForceReset"
```

---

## Task 7: Final validation — class-level test run, format, build

**Files:**
- Possibly modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/RetryStockUpOperationHandlerTests.cs` (formatting only)

- [ ] **Step 1: Run the full test class**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RetryStockUpOperationHandlerTests"`
Expected: `Passed: 5, Failed: 0, Skipped: 0`. Wall-clock under 200 ms (NFR-3).

- [ ] **Step 2: Run `dotnet format` on the test project**

Run: `dotnet format backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: command succeeds with exit code 0. If the new file is reformatted, that is fine — review the diff with `git diff`.

- [ ] **Step 3: Confirm the broader test project still builds clean**

Run: `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-incremental`
Expected: Build succeeded, 0 errors, 0 warnings.

- [ ] **Step 4: Commit any formatting changes (skip if `git status` is clean)**

```bash
git status
# If RetryStockUpOperationHandlerTests.cs shows modifications:
git add backend/test/Anela.Heblo.Tests/Features/Catalog/RetryStockUpOperationHandlerTests.cs
git commit -m "test(catalog): dotnet format RetryStockUpOperationHandlerTests"
```

---

## Self-Review Checklist (done by the plan author)

**Spec coverage**
- FR-1 → Task 2 ✓
- FR-2 → Task 3 ✓
- FR-3 → Task 4 (Warning Times.Never assertion) ✓
- FR-4 → Task 5 (Warning Times.Once assertion) ✓
- FR-5 → Task 6 (Warning Times.Once on Pending) ✓
- NFR-1 (xUnit + Moq + Assert.*, AAA layout, naming, shared mocks in ctor) → Tasks 1–6 ✓
- NFR-2 (`FixedNow` constant, no `DateTime.UtcNow`, no DB) → Task 1 declares constant; all subsequent tasks use it ✓
- NFR-3 (under 200 ms, no I/O) → Task 7 verifies ✓
- NFR-4 (100% line coverage of `Handle`) → all four branches plus entry path are exercised by Tasks 2/3/4/5/6 ✓

**Architecture amendments folded in**
- FR-5 explicit `SaveChangesAsync` Times.Once assertion (arch review §"Specification Amendments" item 1) → Task 6 Step 1 ✓
- Inline comments explaining the Warning-count branch proxy (arch review §"Specification Amendments" item 2) → present in Tasks 4 and 5 ✓
- `using Anela.Heblo.Application.Features.Catalog.Services;` to reach `StockUpResultStatus` (arch review §"Risks and Mitigations" final row) → Task 1 Step 1 ✓

**Placeholder scan**
- No "TBD", "TODO", "similar to Task N", or generic error-handling stubs. Every test method body is fully written out.

**Type / name consistency**
- `RetryStockUpOperationRequest.OperationId` used uniformly (matches `AcceptStockUpOperationRequest.OperationId` in sibling test).
- `RetryStockUpOperationResponse` fields: `Success`, `Status`, `ErrorMessage` — checked in every test.
- `IStockUpOperationRepository.GetByIdAsync(int, CancellationToken)` and `.SaveChangesAsync(CancellationToken)` are the only repository methods touched, consistent across tasks.
- `StockUpOperation` constructor argument order `(documentNumber, productCode, amount, sourceType, sourceId)` identical in all tasks.
- `FixedNow` declared in Task 1 and consumed in Tasks 3 / 4 (and not needed in Tasks 2 / 6).

Plan is internally consistent and complete.
