# GetGiftPackageDetailHandler Error Code Coverage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add unit tests for `GetGiftPackageDetailHandler` that lock in its exception-type-to-`ErrorCodes` mapping (`ArgumentException` → `ValidationError`, any other exception → `InternalServerError`) plus its success path, closing a coverage gap without touching production code.

**Architecture:** One new xUnit test class, `GetGiftPackageDetailHandlerTests`, placed alongside its sibling handler tests in `backend/test/Anela.Heblo.Tests/Application/GiftPackageManufacture/`, mocking the handler's sole dependency `IGiftPackageQueryService` with Moq and asserting with FluentAssertions — following the exact pattern already used by `DisassembleGiftPackageHandlerTests.cs` in the same folder. No production code changes.

**Tech Stack:** .NET 8, xUnit 2.9.2, Moq 4.20.72, FluentAssertions 6.12.0 (all already referenced by `Anela.Heblo.Tests.csproj`).

---

### task: add-detail-handler-error-code-tests

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Application/GiftPackageManufacture/GetGiftPackageDetailHandlerTests.cs`

- [ ] **Step 1: Write the failing/new test file with all three test cases**

Create `backend/test/Anela.Heblo.Tests/Application/GiftPackageManufacture/GetGiftPackageDetailHandlerTests.cs` with this exact content:

```csharp
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts;
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Services;
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.GetGiftPackageDetail;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Application.GiftPackageManufacture;

public class GetGiftPackageDetailHandlerTests
{
    private readonly Mock<IGiftPackageQueryService> _serviceMock = new();

    private GetGiftPackageDetailHandler CreateSut() => new(_serviceMock.Object);

    [Fact]
    public async Task Handle_ReturnsSuccessWithGiftPackage_WhenServiceSucceeds()
    {
        // Arrange
        var giftPackage = new GiftPackageDto
        {
            Code = "SET001",
            Name = "Sample Gift Set"
        };

        _serviceMock
            .Setup(s => s.GetGiftPackageDetailAsync("SET001", 1.0m, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(giftPackage);

        var request = new GetGiftPackageDetailRequest
        {
            GiftPackageCode = "SET001",
            SalesCoefficient = 1.0m
        };

        // Act
        var result = await CreateSut().Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
        result.GiftPackage.Should().NotBeNull();
        result.GiftPackage!.Code.Should().Be("SET001");

        _serviceMock.Verify(
            s => s.GetGiftPackageDetailAsync("SET001", 1.0m, null, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsValidationError_WhenServiceThrowsArgumentException()
    {
        // Arrange
        // Use single-argument constructor — two-argument ctor appends " (Parameter 'name')" to Message.
        _serviceMock
            .Setup(s => s.GetGiftPackageDetailAsync(
                It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Gift package code 'MISSING' not found"));

        var request = new GetGiftPackageDetailRequest
        {
            GiftPackageCode = "MISSING",
            SalesCoefficient = 1.0m
        };

        // Act
        var result = await CreateSut().Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorCode.Should().NotBe(ErrorCodes.InternalServerError);
        result.GiftPackage.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ReturnsInternalServerError_WhenServiceThrowsUnexpectedException()
    {
        // Arrange
        _serviceMock
            .Setup(s => s.GetGiftPackageDetailAsync(
                It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Downstream stock service unavailable"));

        var request = new GetGiftPackageDetailRequest
        {
            GiftPackageCode = "SET001",
            SalesCoefficient = 1.0m
        };

        // Act
        var result = await CreateSut().Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InternalServerError);
        result.ErrorCode.Should().NotBe(ErrorCodes.ValidationError);
        result.GiftPackage.Should().BeNull();
    }
}
```

Notes on the code above (do not deviate without cause):
- `CreateSut()` mirrors `DisassembleGiftPackageHandlerTests.CreateSut()` but `GetGiftPackageDetailHandler` has only one constructor dependency (`IGiftPackageQueryService`) — there is no `ICurrentUserService` involved for this handler, unlike `DisassembleGiftPackageHandler`.
- The success-path test pins the mock setup to the exact argument values the request carries (`"SET001"`, `1.0m`, `null`, `null`) rather than `It.IsAny<...>()`, then verifies that exact call — this is what proves the handler forwards `GiftPackageCode`/`SalesCoefficient`/`FromDate`/`ToDate` through unchanged, matching the sibling test's verification style.
- The two exception-path tests deliberately use `It.IsAny<...>()` on setup (only the thrown exception type matters for those two tests) and each asserts its own exact `ErrorCodes` value plus a `NotBe` on the other code — this is what makes a catch-block swap fail at least one test (spec FR-4).
- `GiftPackageDto.Code`/`Name` are the only fields set for the success fixture; other `GiftPackageDto` properties are irrelevant to this handler's behavior and are left at their defaults.

- [ ] **Step 2: Run the new tests and verify all three pass**

Run:
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetGiftPackageDetailHandlerTests"
```
Expected: `Passed! - Failed: 0, Passed: 3, Skipped: 0` (or equivalent 3/3 pass summary).

Because `GetGiftPackageDetailHandler`'s production logic is already correct (this is a coverage-gap ticket, not a bug fix — see `spec.r1.md` NFR-2 "No production code changes"), all three tests are expected to pass on the first run with no implementation changes. If any test fails, do not "fix" `GetGiftPackageDetailHandler.cs` to make it pass — stop and re-check the test's setup/assertions against the handler's actual current behavior (shown in the excerpt below) before touching production code:

```csharp
// GetGiftPackageDetailHandler.cs, current (unmodified) behavior being tested:
catch (ArgumentException ex)
{
    return new GetGiftPackageDetailResponse { Success = false, ErrorCode = ErrorCodes.ValidationError };
}
catch (Exception ex)
{
    return new GetGiftPackageDetailResponse { Success = false, ErrorCode = ErrorCodes.InternalServerError };
}
```

- [ ] **Step 3: Run the full test project to confirm no regressions**

Run:
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```
Expected: all previously-passing tests still pass; only the count of total tests increases by 3.

- [ ] **Step 4: Format and build check**

Run:
```bash
cd backend
dotnet build
dotnet format --verify-no-changes
```
Expected: build succeeds with no new errors/warnings attributable to the new file; `dotnet format` reports no changes needed (if it does report changes, run `dotnet format` without `--verify-no-changes` to apply them, then re-run Step 2/3 to confirm tests still pass).

- [ ] **Step 5: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Application/GiftPackageManufacture/GetGiftPackageDetailHandlerTests.cs
git commit -m "test: cover GetGiftPackageDetailHandler exception-to-error-code mapping"
```
