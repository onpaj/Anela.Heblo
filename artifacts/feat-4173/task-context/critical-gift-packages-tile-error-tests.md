### task: critical-gift-packages-tile-error-tests

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Logistics/DashboardTiles/CriticalGiftPackagesTileTests.cs`

**Context — production code under test (unchanged, for reference):**
`backend/src/Anela.Heblo.Application/Features/Logistics/DashboardTiles/CriticalGiftPackagesTile.cs`:
```csharp
public class CriticalGiftPackagesTile : ITile
{
    private readonly IMediator _mediator;
    private readonly TimeProvider _timeProvider;

    public CriticalGiftPackagesTile(IMediator mediator, TimeProvider timeProvider)
    {
        _mediator = mediator;
        _timeProvider = timeProvider;
    }

    public async Task<object> LoadDataAsync(Dictionary<string, string>? parameters = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new GetAvailableGiftPackagesRequest { SalesCoefficient = 1.0m };
            var response = await _mediator.Send(request, cancellationToken);

            if (!response.Success)
            {
                return new { status = "error", error = "Failed to load gift packages data" };
            }

            var criticalCount = response.GiftPackages.Count(p => p.Severity == GiftPackageSeverity.Critical);

            return new
            {
                status = "success",
                data = new { count = criticalCount, date = _timeProvider.GetUtcNow().DateTime },
                metadata = new { lastUpdated = _timeProvider.GetUtcNow().DateTime, source = "GiftPackageManufacture" },
                drillDown = new { filters = new { severity = "Critical" }, enabled = true, tooltip = "..." }
            };
        }
        catch (Exception ex)
        {
            return new { status = "error", error = ex.Message };
        }
    }
}
```

`GetAvailableGiftPackagesResponse` (`Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.GetAvailableGiftPackages`):
```csharp
public class GetAvailableGiftPackagesResponse : BaseResponse
{
    public List<GiftPackageDto> GiftPackages { get; set; } = new();
}
```
**Verified:** this class defines **no** `(ErrorCodes, ...)` constructor of its own (unlike `GetPurchaseStockAnalysisResponse`, which does). `BaseResponse.Success` has a `public` setter, so the correct way to build an unsuccessful response here is an object initializer: `new GetAvailableGiftPackagesResponse { Success = false }`. Do **not** attempt `new GetAvailableGiftPackagesResponse(ErrorCodes.X)` — it will not compile.

- [ ] **Step 1: Write the test file (compile-failure red step)**

Create `backend/test/Anela.Heblo.Tests/Features/Logistics/DashboardTiles/CriticalGiftPackagesTileTests.cs` with this exact content:

```csharp
using System.Text.Json;
using Anela.Heblo.Application.Features.Logistics.DashboardTiles;
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.GetAvailableGiftPackages;
using FluentAssertions;
using MediatR;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.DashboardTiles;

public class CriticalGiftPackagesTileTests
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly CriticalGiftPackagesTile _tile;

    public CriticalGiftPackagesTileTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _timeProviderMock = new Mock<TimeProvider>();
        _tile = new CriticalGiftPackagesTile(_mediatorMock.Object, _timeProviderMock.Object);
    }

    [Fact]
    public async Task LoadDataAsync_WhenResponseNotSuccessful_ReturnsErrorStatus()
    {
        // Arrange
        var response = new GetAvailableGiftPackagesResponse { Success = false };

        _mediatorMock
            .Setup(x => x.Send(It.IsAny<GetAvailableGiftPackagesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _tile.LoadDataAsync();

        // Assert
        var json = JsonSerializer.SerializeToElement(result);

        json.GetProperty("status").GetString().Should().Be("error");
        json.GetProperty("error").GetString().Should().Be("Failed to load gift packages data");
        json.TryGetProperty("data", out _).Should().BeFalse();

        _mediatorMock.Verify(
            x => x.Send(It.IsAny<GetAvailableGiftPackagesRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task LoadDataAsync_WhenServiceThrows_ReturnsExceptionErrorStatus()
    {
        // Arrange
        _mediatorMock
            .Setup(x => x.Send(It.IsAny<GetAvailableGiftPackagesRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated gift package service failure"));

        // Act
        var result = await _tile.LoadDataAsync();

        // Assert
        var json = JsonSerializer.SerializeToElement(result);

        json.GetProperty("status").GetString().Should().Be("error");
        json.GetProperty("error").GetString().Should().Be("Simulated gift package service failure");
        json.TryGetProperty("data", out _).Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run the tests to verify the file compiles and both tests pass**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CriticalGiftPackagesTileTests"
```
Expected: **PASS** — 2 tests run, 2 passed, 0 failed. (Not a red step in the classic TDD sense: the production code already implements both branches correctly, so once the test file compiles, both assertions should be true immediately. If either test fails, that is new information — either this plan's understanding of the production shape is wrong, or the tests need adjustment; do not modify `CriticalGiftPackagesTile.cs` to make a test pass, since the issue and spec explicitly scope this as test-only.)

- [ ] **Step 3: Run the full backend test suite to confirm no regressions**

Run:
```bash
cd backend && dotnet test
```
Expected: all tests pass (the new 2 tests plus the full existing suite), no new failures.

- [ ] **Step 4: Format and build check**

Run:
```bash
cd backend && dotnet format --verify-no-changes
cd backend && dotnet build
```
Expected: `dotnet format --verify-no-changes` reports no files needing changes (if it does, run `dotnet format` without `--verify-no-changes` and re-stage); `dotnet build` succeeds with no errors.

- [ ] **Step 5: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Logistics/DashboardTiles/CriticalGiftPackagesTileTests.cs
git commit -m "test(logistics): cover CriticalGiftPackagesTile error-shape branches

Adds unit tests for the two untested branches in CriticalGiftPackagesTile.LoadDataAsync:
the !response.Success early-return and the outer catch(Exception) handler. Both are
asserted to return a { status: \"error\", error: ... } shape with no data property,
closing issue #4173 (0.0% line coverage -> covered).

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_018zvX5hB6VCjJobkgfuj3bC"
```

---

## Self-Review

**1. Spec coverage:**
- FR-1 (`!response.Success` branch test) → covered by `LoadDataAsync_WhenResponseNotSuccessful_ReturnsErrorStatus`.
- FR-2 (`catch (Exception)` branch test, asserting `ex.Message`) → covered by `LoadDataAsync_WhenServiceThrows_ReturnsExceptionErrorStatus`.
- FR-3 (shape-parity: no `data` property leaks in either error shape) → covered by the `TryGetProperty("data", out _).Should().BeFalse()` assertion in both tests, per arch-review's Specification Amendment (folded into the two tests rather than a separate third test).
- Out-of-scope items (success-path test, production code changes, other tiles) → correctly not touched by this plan.

**2. Placeholder scan:** No "TBD"/"TODO"/"implement later" placeholders. All code blocks are complete, compilable C#. Commands have explicit expected output.

**3. Type consistency:** `CriticalGiftPackagesTile`, `GetAvailableGiftPackagesRequest`, `GetAvailableGiftPackagesResponse`, `IMediator`, `TimeProvider` are used identically to their actual signatures verified by reading the source files during the architecting phase. The `GetAvailableGiftPackagesResponse { Success = false }` object-initializer approach was verified against the actual class definition (no `ErrorCodes` constructor exists on this response type, correcting an open risk flagged in arch-review.r1.md).

No gaps found; plan is complete as a single task.
