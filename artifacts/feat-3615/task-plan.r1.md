# Unit test coverage for RecalculateProductWeightHandler — Implementation Plan

**Goal:** Raise `RecalculateProductWeightHandler` line coverage from 17.1% above the 60% CI threshold by adding a single xUnit test file that exercises all four functional requirements: single-product dispatch (FR-1), full-catalog dispatch (FR-2), `Success` flag derivation (FR-3), and the exception fallback path (FR-4). No production code changes.

**Architecture:** One new test class, `RecalculateProductWeightHandlerTests`, in the existing `Anela.Heblo.Tests` project. It mirrors the canonical `CreateManufactureDifficultyHandlerTests` pattern: `private readonly` mocks and a single system-under-test built once in the constructor. The only behavioral collaborator, `IProductWeightRecalculationService`, is mocked with Moq (`ReturnsAsync` / `ThrowsAsync`); `ILogger<RecalculateProductWeightHandler>` is mocked passively and never asserted. Dispatch is pinned with paired `Verify(...)` (`Times.Once` on the expected method AND `Times.Never` on its sibling). Response fields are asserted with FluentAssertions.

**Tech Stack:** .NET 8, xUnit (`[Fact]`/`[Theory]`), Moq, FluentAssertions — all already referenced by `Anela.Heblo.Tests.csproj`. No new NuGet packages, no config, no migrations.

**Verified real type shapes (do not re-derive):**
- `RecalculateProductWeightRequest : IRequest<RecalculateProductWeightResponse>` — `public string? ProductCode { get; set; }`.
- `RecalculateProductWeightResponse : BaseResponse` — `int ProcessedCount`, `int SuccessCount`, `int ErrorCount`, `List<string> ErrorMessages`. `Success` is inherited from `BaseResponse` and **defaults to `true`**.
- `ProductWeightRecalculationResult` — `int ProcessedCount`, `int SuccessCount`, `int ErrorCount`, `List<string> ErrorMessages`, `TimeSpan Duration`, `DateTime StartTime`, `DateTime EndTime`.
- `IProductWeightRecalculationService` (namespace `Anela.Heblo.Application.Features.Catalog.Services`):
  - `Task<ProductWeightRecalculationResult> RecalculateAllProductWeights(CancellationToken cancellationToken = default)`
  - `Task<ProductWeightRecalculationResult> RecalculateProductWeight(string productCode, CancellationToken cancellationToken = default)`
- Handler under test: `RecalculateProductWeightHandler.Handle(RecalculateProductWeightRequest, CancellationToken)` in namespace `Anela.Heblo.Application.Features.Catalog.UseCases.RecalculateProductWeight`. On exception it returns `ProcessedCount = 0`, `SuccessCount = 0`, `ErrorCount = 1`, `ErrorMessages = { $"Internal error: {ex.Message}" }`, `Success = false`.

---

### task: add-recalculate-product-weight-handler-tests

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/RecalculateProductWeightHandlerTests.cs`

All paths below are relative to the repository root `/home/user/worktrees/feature-3615-Coverage-Gap-Catalog-Recalculateproductweighthandl`. Run every `dotnet` command from the `backend/` directory.

- [ ] **Step 1: Write the complete failing test file**

Create `backend/test/Anela.Heblo.Tests/Features/Catalog/RecalculateProductWeightHandlerTests.cs` with exactly this content:

```csharp
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Application.Features.Catalog.UseCases.RecalculateProductWeight;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class RecalculateProductWeightHandlerTests
{
    private readonly Mock<IProductWeightRecalculationService> _serviceMock;
    private readonly Mock<ILogger<RecalculateProductWeightHandler>> _loggerMock;
    private readonly RecalculateProductWeightHandler _handler;

    public RecalculateProductWeightHandlerTests()
    {
        _serviceMock = new Mock<IProductWeightRecalculationService>();
        _loggerMock = new Mock<ILogger<RecalculateProductWeightHandler>>();
        _handler = new RecalculateProductWeightHandler(_serviceMock.Object, _loggerMock.Object);
    }

    // FR-1: Single-product recalculation path
    [Fact]
    public async Task Handle_WithProductCode_DispatchesToSingleProductRecalculation()
    {
        // Arrange
        var request = new RecalculateProductWeightRequest { ProductCode = "PROD001" };
        var serviceResult = new ProductWeightRecalculationResult
        {
            ProcessedCount = 1,
            SuccessCount = 1,
            ErrorCount = 0,
            ErrorMessages = new List<string>()
        };
        _serviceMock
            .Setup(s => s.RecalculateProductWeight("PROD001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(serviceResult);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert - dispatch pinned in both directions
        _serviceMock.Verify(
            s => s.RecalculateProductWeight("PROD001", It.IsAny<CancellationToken>()),
            Times.Once);
        _serviceMock.Verify(
            s => s.RecalculateAllProductWeights(It.IsAny<CancellationToken>()),
            Times.Never);

        // Assert - response mirrors the service result
        response.ProcessedCount.Should().Be(1);
        response.SuccessCount.Should().Be(1);
        response.ErrorCount.Should().Be(0);
        response.ErrorMessages.Should().BeEquivalentTo(serviceResult.ErrorMessages);
    }

    // FR-2: Full-catalog recalculation path (null and empty are one equivalence class)
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Handle_WithoutProductCode_DispatchesToFullCatalogRecalculation(string? productCode)
    {
        // Arrange
        var request = new RecalculateProductWeightRequest { ProductCode = productCode };
        var serviceResult = new ProductWeightRecalculationResult
        {
            ProcessedCount = 42,
            SuccessCount = 42,
            ErrorCount = 0,
            ErrorMessages = new List<string>()
        };
        _serviceMock
            .Setup(s => s.RecalculateAllProductWeights(It.IsAny<CancellationToken>()))
            .ReturnsAsync(serviceResult);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert - dispatch pinned in both directions
        _serviceMock.Verify(
            s => s.RecalculateAllProductWeights(It.IsAny<CancellationToken>()),
            Times.Once);
        _serviceMock.Verify(
            s => s.RecalculateProductWeight(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // Assert - response mirrors the service result
        response.ProcessedCount.Should().Be(42);
        response.SuccessCount.Should().Be(42);
        response.ErrorCount.Should().Be(0);
    }

    // FR-3: Success flag derivation - no errors -> Success == true
    [Fact]
    public async Task Handle_WhenServiceReturnsNoErrors_SetsSuccessTrue()
    {
        // Arrange
        var request = new RecalculateProductWeightRequest { ProductCode = null };
        _serviceMock
            .Setup(s => s.RecalculateAllProductWeights(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductWeightRecalculationResult
            {
                ProcessedCount = 10,
                SuccessCount = 10,
                ErrorCount = 0,
                ErrorMessages = new List<string>()
            });

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
    }

    // FR-3: Success flag derivation - errors present -> Success == false, counts/messages pass through.
    // Load-bearing because BaseResponse.Success defaults to true: this proves the handler's
    // `Success = result.ErrorCount == 0` line actually executed and flipped the default.
    [Fact]
    public async Task Handle_WhenServiceReturnsErrors_SetsSuccessFalseAndPassesThrough()
    {
        // Arrange
        var request = new RecalculateProductWeightRequest { ProductCode = null };
        var errorMessages = new List<string> { "Product XYZ has no ingredients" };
        _serviceMock
            .Setup(s => s.RecalculateAllProductWeights(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductWeightRecalculationResult
            {
                ProcessedCount = 10,
                SuccessCount = 9,
                ErrorCount = 1,
                ErrorMessages = errorMessages
            });

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ProcessedCount.Should().Be(10);
        response.SuccessCount.Should().Be(9);
        response.ErrorCount.Should().Be(1);
        response.ErrorMessages.Should().BeEquivalentTo(errorMessages);
    }

    // FR-4: Exception fallback path (catch wraps both branches; exercised via empty ProductCode)
    [Fact]
    public async Task Handle_WhenServiceThrows_ReturnsFallbackResponseWithoutRethrowing()
    {
        // Arrange
        var request = new RecalculateProductWeightRequest { ProductCode = null };
        _serviceMock
            .Setup(s => s.RecalculateAllProductWeights(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("boom"));

        // Act - awaiting a returned response (not throwing) is itself proof the handler did not rethrow
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.ProcessedCount.Should().Be(0);
        response.SuccessCount.Should().Be(0);
        response.ErrorCount.Should().Be(1);
        response.Success.Should().BeFalse();
        response.ErrorMessages.Should().ContainSingle();
        response.ErrorMessages.Should().Contain(m => m.Contains("Internal error") && m.Contains("boom"));
    }
}
```

- [ ] **Step 2: Confirm the file compiles and the tests pass**

The SUT and all collaborators already exist, so this "failing test" step is a compile-and-run gate rather than a red-phase assertion failure (there is no production code to write — the behavior under test is already implemented). Run:

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RecalculateProductWeightHandlerTests"`

Expected: build succeeds and the run reports `Passed!  - Failed: 0, Passed: 6` (5 test methods, one of which is a `[Theory]` with 2 cases → 6 executed test cases). If the build fails on a missing `using` or a member-name mismatch, fix it against the verified shapes at the top of this plan — do not change production code.

- [ ] **Step 3: Verify the coverage gap is closed**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RecalculateProductWeightHandlerTests" --collect:"XPlat Code Coverage"`

Expected: the run passes and produces a `coverage.cobertura.xml` under `backend/test/Anela.Heblo.Tests/TestResults/<guid>/`. The tests exercise both dispatch branches, the mapping block, the `Success = result.ErrorCount == 0` line (both true and false), and the catch block — i.e. all reachable lines of `RecalculateProductWeightHandler.Handle`, bringing it well above the 60% threshold. (Reading the exact percentage from the XML is optional; the branch/line coverage is guaranteed by the tests above.)

- [ ] **Step 4: Format check**

Run: `dotnet format backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --verify-no-changes`

Expected: exits `0` with no reported changes. If it reports formatting differences, run `dotnet format backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` and re-run the verify command until it is clean.

- [ ] **Step 5: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/RecalculateProductWeightHandlerTests.cs
git commit -m "test: cover RecalculateProductWeightHandler dispatch, mapping, and fallback

Adds unit tests for single-product dispatch (FR-1), full-catalog dispatch
(FR-2), Success flag derivation (FR-3), and the exception fallback path
(FR-4), raising handler coverage above the 60% CI threshold.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_018ARxdYPmo5CuiZohS2PbaS"
```

---

## Self-review

- **FR-1 (single-product dispatch):** `Handle_WithProductCode_DispatchesToSingleProductRecalculation` — asserts `RecalculateProductWeight("PROD001", ...)` `Times.Once`, `RecalculateAllProductWeights` `Times.Never`, and mirrors `ProcessedCount`/`SuccessCount`/`ErrorCount`/`ErrorMessages`. ✓
- **FR-2 (full-catalog dispatch):** `Handle_WithoutProductCode_DispatchesToFullCatalogRecalculation` — `[Theory]` over `null` and `""`; asserts `RecalculateAllProductWeights` `Times.Once`, `RecalculateProductWeight` `Times.Never`. ✓
- **FR-3 (Success derivation):** `Handle_WhenServiceReturnsNoErrors_SetsSuccessTrue` (`ErrorCount = 0` → `Success == true`) and `Handle_WhenServiceReturnsErrors_SetsSuccessFalseAndPassesThrough` (`ErrorCount = 1` with non-empty messages → `Success == false` plus pass-through of counts/messages — load-bearing against the `true` default). ✓
- **FR-4 (exception fallback):** `Handle_WhenServiceThrows_ReturnsFallbackResponseWithoutRethrowing` — service `ThrowsAsync(new Exception("boom"))` on the empty-`ProductCode` branch; asserts no rethrow, `ProcessedCount == 0`, `SuccessCount == 0`, `ErrorCount == 1`, `Success == false`, and a single `ErrorMessages` entry containing both `"Internal error"` and `"boom"` (substring, not exact-string, per the arch-review risk mitigation). ✓
- **Conventions (NFR-3):** namespace `Anela.Heblo.Tests.Features.Catalog`; `private readonly` mocks + single SUT built in the constructor; xUnit + Moq + FluentAssertions; logger mocked but never asserted; file at the required path. ✓
- **No production changes / no new packages (Out of Scope):** only one test file created. ✓
- **No placeholders:** every step shows exact code, exact commands, and expected output. ✓
