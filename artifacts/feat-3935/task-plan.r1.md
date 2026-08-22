# Test Coverage for DeleteManufactureDifficultyHandler Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Raise `DeleteManufactureDifficultyHandler` from 23.7% to full-branch line coverage by adding a unit test class covering its not-found, happy-path, and exception execution paths — test-only, no production code changes.

**Architecture:** One new xUnit test class, `DeleteManufactureDifficultyHandlerTests`, in the existing `backend/test/Anela.Heblo.Tests/Features/Catalog/` folder, following the exact Moq + FluentAssertions pattern already used by sibling `UpdateManufactureDifficultyHandlerTests.cs` and `CreateManufactureDifficultyHandlerTests.cs`. Because the handler's production behavior is already correct and is not being changed, each task below writes a test asserting the handler's **current, correct** behavior and runs it to confirm it **passes** immediately (the usual "run to see it fail first" TDD step does not apply to a test-only coverage task — there is no new production code to make the test go from red to green). Each task adds one or two `[Fact]` methods to the same growing file.

**Tech Stack:** .NET 8, xUnit, Moq, FluentAssertions — no new packages.

---

### task: setup-test-file

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/DeleteManufactureDifficultyHandlerTests.cs`

- [ ] **Step 1: Create the test file skeleton**

Create the file with the class scaffold, matching the constructor shape confirmed in the architecture review (3 dependencies only — no `IMapper`, no `TimeProvider`):

```csharp
using Anela.Heblo.Application.Features.Catalog.UseCases.DeleteManufactureDifficulty;
using Anela.Heblo.Domain.Features.Catalog;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class DeleteManufactureDifficultyHandlerTests
{
    private readonly Mock<IManufactureDifficultyRepository> _repositoryMock;
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly Mock<ILogger<DeleteManufactureDifficultyHandler>> _loggerMock;
    private readonly DeleteManufactureDifficultyHandler _handler;

    public DeleteManufactureDifficultyHandlerTests()
    {
        _repositoryMock = new Mock<IManufactureDifficultyRepository>();
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _loggerMock = new Mock<ILogger<DeleteManufactureDifficultyHandler>>();

        _handler = new DeleteManufactureDifficultyHandler(
            _repositoryMock.Object,
            _catalogRepositoryMock.Object,
            _loggerMock.Object);
    }
}
```

- [ ] **Step 2: Build to confirm the skeleton compiles**

Run: `cd backend && dotnet build test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: `Build succeeded.` (0 errors) — an empty test class with no `[Fact]` methods is valid.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/DeleteManufactureDifficultyHandlerTests.cs
git commit -m "test(catalog): scaffold DeleteManufactureDifficultyHandlerTests"
```

---

### task: not-found-path-test

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/DeleteManufactureDifficultyHandlerTests.cs` (append inside the class body, after the constructor)

Covers spec FR-1.

- [ ] **Step 1: Write the test**

Add this `[Fact]` inside the class body:

```csharp
    [Fact]
    public async Task Handle_NotFound_ReturnsFailureAndPerformsNoFurtherWork()
    {
        // Arrange
        var request = new DeleteManufactureDifficultyRequest { Id = 42 };

        _repositoryMock
            .Setup(r => r.GetByIdAsync(request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureDifficultySetting?)null);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.Message.Should().Be("ManufactureDifficultyHistory with ID 42 not found");

        _repositoryMock.Verify(
            r => r.DeleteAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _catalogRepositoryMock.Verify(
            r => r.RefreshManufactureDifficultySettingsData(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
```

- [ ] **Step 2: Run the test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DeleteManufactureDifficultyHandlerTests.Handle_NotFound_ReturnsFailureAndPerformsNoFurtherWork"`
Expected: `Passed! - Failed: 0, Passed: 1`

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/DeleteManufactureDifficultyHandlerTests.cs
git commit -m "test(catalog): cover DeleteManufactureDifficultyHandler not-found path"
```

---

### task: happy-path-cache-refresh-test

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/DeleteManufactureDifficultyHandlerTests.cs` (append inside the class body)

Covers spec FR-2 — this is the test that directly guards against the coverage-gap issue's stated risk (cache-refresh dropped or given the wrong `productCode`).

- [ ] **Step 1: Write the test**

Add this `[Fact]` inside the class body:

```csharp
    [Fact]
    public async Task Handle_ExistingEntry_DeletesRefreshesCacheInOrderAndReturnsSuccess()
    {
        // Arrange
        var request = new DeleteManufactureDifficultyRequest { Id = 11 };
        var existing = new ManufactureDifficultySetting
        {
            Id = 11,
            ProductCode = "PROD-HAPPY",
            DifficultyValue = 2,
            ValidFrom = new DateTime(2024, 1, 1),
            ValidTo = new DateTime(2024, 12, 31)
        };

        _repositoryMock
            .Setup(r => r.GetByIdAsync(request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var callSequence = new MockSequence();
        _repositoryMock
            .InSequence(callSequence)
            .Setup(r => r.DeleteAsync(request.Id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _catalogRepositoryMock
            .InSequence(callSequence)
            .Setup(r => r.RefreshManufactureDifficultySettingsData(existing.ProductCode, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.Message.Should().Be("Manufacture difficulty deleted successfully");

        _repositoryMock.Verify(
            r => r.DeleteAsync(request.Id, It.IsAny<CancellationToken>()),
            Times.Once);

        // Crux of the original coverage gap: the cache refresh must receive the
        // deleted entity's ProductCode, not any value derived from the request.
        _catalogRepositoryMock.Verify(
            r => r.RefreshManufactureDifficultySettingsData(existing.ProductCode, It.IsAny<CancellationToken>()),
            Times.Once);
    }
```

Note: because both mock setups are registered `InSequence(callSequence)`, Moq will throw a `MockException` at invocation time if `RefreshManufactureDifficultySettingsData` is ever called before `DeleteAsync` — this is what proves the ordering requirement from FR-2, not a separate assertion.

- [ ] **Step 2: Run the test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DeleteManufactureDifficultyHandlerTests.Handle_ExistingEntry_DeletesRefreshesCacheInOrderAndReturnsSuccess"`
Expected: `Passed! - Failed: 0, Passed: 1`

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/DeleteManufactureDifficultyHandlerTests.cs
git commit -m "test(catalog): cover DeleteManufactureDifficultyHandler delete+cache-refresh happy path"
```

---

### task: exception-path-tests

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/DeleteManufactureDifficultyHandlerTests.cs` (append inside the class body)

Covers spec FR-3, both cases (A: `DeleteAsync` throws; B: `RefreshManufactureDifficultySettingsData` throws).

- [ ] **Step 1: Write the DeleteAsync-throws test**

Add this `[Fact]` inside the class body:

```csharp
    [Fact]
    public async Task Handle_DeleteAsyncThrows_ReturnsFailureWithoutPropagating()
    {
        // Arrange
        var request = new DeleteManufactureDifficultyRequest { Id = 5 };
        var existing = new ManufactureDifficultySetting
        {
            Id = 5,
            ProductCode = "PROD-ERR",
            DifficultyValue = 1,
            ValidFrom = new DateTime(2024, 1, 1),
            ValidTo = new DateTime(2024, 12, 31)
        };

        _repositoryMock
            .Setup(r => r.GetByIdAsync(request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _repositoryMock
            .Setup(r => r.DeleteAsync(request.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("delete boom"));

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.Message.Should().Contain("delete boom");

        _catalogRepositoryMock.Verify(
            r => r.RefreshManufactureDifficultySettingsData(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
```

- [ ] **Step 2: Run the test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DeleteManufactureDifficultyHandlerTests.Handle_DeleteAsyncThrows_ReturnsFailureWithoutPropagating"`
Expected: `Passed! - Failed: 0, Passed: 1`

- [ ] **Step 3: Write the RefreshManufactureDifficultySettingsData-throws test**

Add this `[Fact]` inside the class body:

```csharp
    [Fact]
    public async Task Handle_RefreshCacheThrows_ReturnsFailureWithoutPropagating()
    {
        // Arrange
        var request = new DeleteManufactureDifficultyRequest { Id = 6 };
        var existing = new ManufactureDifficultySetting
        {
            Id = 6,
            ProductCode = "PROD-ERR2",
            DifficultyValue = 1,
            ValidFrom = new DateTime(2024, 1, 1),
            ValidTo = new DateTime(2024, 12, 31)
        };

        _repositoryMock
            .Setup(r => r.GetByIdAsync(request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _repositoryMock
            .Setup(r => r.DeleteAsync(request.Id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _catalogRepositoryMock
            .Setup(r => r.RefreshManufactureDifficultySettingsData(existing.ProductCode, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("refresh boom"));

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.Message.Should().Contain("refresh boom");

        // Proves the throw happened after delete succeeded, not instead of it.
        _repositoryMock.Verify(
            r => r.DeleteAsync(request.Id, It.IsAny<CancellationToken>()),
            Times.Once);
    }
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DeleteManufactureDifficultyHandlerTests.Handle_RefreshCacheThrows_ReturnsFailureWithoutPropagating"`
Expected: `Passed! - Failed: 0, Passed: 1`

- [ ] **Step 5: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/DeleteManufactureDifficultyHandlerTests.cs
git commit -m "test(catalog): cover DeleteManufactureDifficultyHandler exception paths"
```

---

### task: full-suite-and-coverage-verification

**Files:**
- None created or modified — verification only.

- [ ] **Step 1: Run the full new test class**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DeleteManufactureDifficultyHandlerTests"`
Expected: `Passed! - Failed: 0, Passed: 4, Skipped: 0`

- [ ] **Step 2: Run the full backend test suite to confirm no regressions**

Run: `cd backend && dotnet test`
Expected: all tests pass (no new failures introduced by the added file; the file is additive-only and does not touch shared fixtures).

- [ ] **Step 3: Run `dotnet format` and `dotnet build` per repository validation requirements**

Run: `cd backend && dotnet format && dotnet build`
Expected: `dotnet format` reports no changes needed (or auto-fixes whitespace/using-order in the new file only); `dotnet build` succeeds with 0 errors.

- [ ] **Step 4: Confirm coverage improvement**

If the project's coverage tooling is run locally (e.g. `dotnet test /p:CollectCoverage=true` or the CI coverage script referenced by the original issue), confirm `DeleteManufactureDifficultyHandler.cs` line coverage now exceeds the 60% CI filter threshold (all three branches — not-found, happy path with sequencing, both exception cases — are now exercised, which covers effectively 100% of the handler's lines).

- [ ] **Step 5: Final commit (if step 3 produced formatting changes)**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/DeleteManufactureDifficultyHandlerTests.cs
git commit -m "test(catalog): apply dotnet format to DeleteManufactureDifficultyHandlerTests" || true
```

---

## Self-Review

**1. Spec coverage:** FR-1 → `not-found-path-test`. FR-2 → `happy-path-cache-refresh-test` (including the ordering requirement via `MockSequence`, and the exact-`ProductCode` requirement via `Verify(existing.ProductCode, ...)`). FR-3 case A and case B → `exception-path-tests`. NFR-1/NFR-2 are N/A per spec, no task needed. All FRs have a corresponding task.

**2. Placeholder scan:** No "TBD"/"implement later"/"add appropriate error handling" phrases present. Every step shows complete, runnable code or an exact command with expected output.

**3. Type consistency:** `DeleteManufactureDifficultyRequest.Id` (int), `ManufactureDifficultySetting.ProductCode` (string), `DeleteManufactureDifficultyResponse.Success`/`Message` are used identically across all four test methods and match the production types read directly from `DeleteManufactureDifficultyHandler.cs`, `DeleteManufactureDifficultyRequest.cs`, `DeleteManufactureDifficultyResponse.cs`, and `IManufactureDifficultyRepository.cs` during architecture review. No naming drift between tasks.
