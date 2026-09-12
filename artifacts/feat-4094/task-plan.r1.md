# Implementation Plan: Unit Tests for ClearFlagOverrideHandler

## Overview
`ClearFlagOverrideHandler` (Feature Flags module) has 0% line coverage against a 60% threshold. This plan adds a single new xUnit test class, `ClearFlagOverrideHandlerTests`, that exercises both branches of `ClearFlagOverrideHandler.Handle` — the "override not found" short-circuit and the "override deleted, cache invalidated" success path — using Moq for `IFeatureFlagOverrideRepository` and `IMemoryCache`. No production code changes are made.

### task: add-clear-flag-override-handler-tests
**Goal:** Create `ClearFlagOverrideHandlerTests` with two `[Fact]` tests covering the not-found and successful-delete branches of `ClearFlagOverrideHandler.Handle`, verifying the returned response, the repository call (key + cancellation token passthrough), and the exact cache-invalidation call (or its absence), closing the 0%-coverage gap on this handler.

**Files:**
- `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/UseCases/ClearFlagOverride/ClearFlagOverrideHandlerTests.cs` — create: new test file, namespace `Anela.Heblo.Tests.Features.FeatureFlags.UseCases.ClearFlagOverride`, containing the `ClearFlagOverrideHandlerTests` class described below. This is a brand-new directory (`UseCases/ClearFlagOverride/`) under `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/`, mirroring the source layout at `backend/src/Anela.Heblo.Application/Features/FeatureFlags/UseCases/ClearFlagOverride/`.

No other files are created or modified. The handler under test (`backend/src/Anela.Heblo.Application/Features/FeatureFlags/UseCases/ClearFlagOverride/ClearFlagOverrideHandler.cs`), its request/response types (`ClearFlagOverrideRequest.cs`, same folder), `IFeatureFlagOverrideRepository` (`backend/src/Anela.Heblo.Domain/Features/FeatureFlags/IFeatureFlagOverrideRepository.cs`), and `HebloFeatureProvider.CacheKey` (`backend/src/Anela.Heblo.Application/Features/FeatureFlags/Infrastructure/HebloFeatureProvider.cs:23`) are consumed as-is and must not be touched.

**Reference — the exact code under test today (unchanged by this task):**
```csharp
// backend/src/Anela.Heblo.Application/Features/FeatureFlags/UseCases/ClearFlagOverride/ClearFlagOverrideHandler.cs
internal sealed class ClearFlagOverrideHandler : IRequestHandler<ClearFlagOverrideRequest, ClearFlagOverrideResponse>
{
    private readonly IFeatureFlagOverrideRepository _repo;
    private readonly IMemoryCache _cache;

    public ClearFlagOverrideHandler(IFeatureFlagOverrideRepository repo, IMemoryCache cache)
    {
        _repo = repo;
        _cache = cache;
    }

    public async Task<ClearFlagOverrideResponse> Handle(
        ClearFlagOverrideRequest request, CancellationToken ct)
    {
        var deleted = await _repo.DeleteAsync(request.Key, ct);
        if (!deleted)
            return new ClearFlagOverrideResponse(ErrorCodes.ResourceNotFound);
        _cache.Remove(HebloFeatureProvider.CacheKey);
        return new ClearFlagOverrideResponse();
    }
}

// backend/src/Anela.Heblo.Application/Features/FeatureFlags/UseCases/ClearFlagOverride/ClearFlagOverrideRequest.cs
public class ClearFlagOverrideRequest : IRequest<ClearFlagOverrideResponse>
{
    public string Key { get; set; } = "";
}

public class ClearFlagOverrideResponse : BaseResponse
{
    public ClearFlagOverrideResponse() { }
    public ClearFlagOverrideResponse(ErrorCodes errorCode) : base(errorCode) { }
}

// backend/src/Anela.Heblo.Application/Shared/BaseResponse.cs (relevant members)
public abstract class BaseResponse
{
    public bool Success { get; set; } = true;
    public ErrorCodes? ErrorCode { get; set; }
    public Dictionary<string, string>? Params { get; set; }
    protected BaseResponse() { Success = true; }
    protected BaseResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
    {
        Success = false;
        ErrorCode = errorCode;
        Params = parameters;
    }
}

// backend/src/Anela.Heblo.Domain/Features/FeatureFlags/IFeatureFlagOverrideRepository.cs (relevant member)
public interface IFeatureFlagOverrideRepository
{
    // ...
    Task<bool> DeleteAsync(string key, CancellationToken ct = default);
}

// backend/src/Anela.Heblo.Application/Features/FeatureFlags/Infrastructure/HebloFeatureProvider.cs:23
internal const string CacheKey = "feature_flag_overrides";
```
Note: `ErrorCode` on `BaseResponse` is a nullable enum (`ErrorCodes? ErrorCode`), not a string — assert with `.Should().Be(ErrorCodes.ResourceNotFound)` / `.Should().BeNull()` accordingly (do not compare to a string literal).

**Steps:**

1. Create the directory `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/UseCases/ClearFlagOverride/` and write the new test file with the following complete content:

```csharp
using Anela.Heblo.Application.Features.FeatureFlags.Infrastructure;
using Anela.Heblo.Application.Features.FeatureFlags.UseCases.ClearFlagOverride;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.FeatureFlags;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.FeatureFlags.UseCases.ClearFlagOverride;

public class ClearFlagOverrideHandlerTests
{
    private readonly Mock<IFeatureFlagOverrideRepository> _repoMock;
    private readonly Mock<IMemoryCache> _cacheMock;
    private readonly ClearFlagOverrideHandler _handler;

    public ClearFlagOverrideHandlerTests()
    {
        _repoMock = new Mock<IFeatureFlagOverrideRepository>();
        _cacheMock = new Mock<IMemoryCache>();
        _handler = new ClearFlagOverrideHandler(_repoMock.Object, _cacheMock.Object);
    }

    [Fact]
    public async Task Handle_OverrideNotFound_ReturnsResourceNotFound_AndDoesNotTouchCache()
    {
        var request = new ClearFlagOverrideRequest { Key = "some-flag-key" };
        _repoMock.Setup(r => r.DeleteAsync(request.Key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
        _repoMock.Verify(r => r.DeleteAsync(request.Key, It.IsAny<CancellationToken>()), Times.Once);
        _cacheMock.Verify(c => c.Remove(It.IsAny<object>()), Times.Never);
    }

    [Fact]
    public async Task Handle_OverrideDeleted_InvalidatesCache_AndReturnsSuccess()
    {
        var request = new ClearFlagOverrideRequest { Key = "some-flag-key" };
        _repoMock.Setup(r => r.DeleteAsync(request.Key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
        _repoMock.Verify(r => r.DeleteAsync(request.Key, It.IsAny<CancellationToken>()), Times.Once);
        _cacheMock.Verify(c => c.Remove(HebloFeatureProvider.CacheKey), Times.Once);
    }
}
```

2. Confirm the new file compiles standalone in context: `IFeatureFlagOverrideRepository` resolves from `Anela.Heblo.Domain.Features.FeatureFlags`, `ClearFlagOverrideHandler`/`ClearFlagOverrideRequest` from `Anela.Heblo.Application.Features.FeatureFlags.UseCases.ClearFlagOverride`, `HebloFeatureProvider` (for its `internal const string CacheKey`) from `Anela.Heblo.Application.Features.FeatureFlags.Infrastructure`, and `ErrorCodes`/`BaseResponse` from `Anela.Heblo.Application.Shared`. `HebloFeatureProvider` is `internal`; this compiles because `Anela.Heblo.Application`'s `AssemblyInfo.cs` already declares `InternalsVisibleTo("Anela.Heblo.Tests")` (already proven by `HebloFeatureProviderTests.cs` referencing the same type from the same test project — no assembly/project configuration change is needed).

3. Build the backend to catch compile errors early:
   `cd backend && dotnet build`
   Expected: build succeeds with 0 errors.

4. Run just the new test class:
   `cd backend && dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~ClearFlagOverrideHandlerTests"`
   Expected: 2 tests discovered, 2 passed, 0 failed.

5. Run `dotnet format` to ensure the new file matches repo formatting conventions:
   `cd backend && dotnet format`
   Expected: no unexpected changes outside the new file (the new file itself may be reformatted to match style — re-check its content afterward if `dotnet format` alters it).

6. Run the full FeatureFlags-module test slice to confirm nothing else in the module regressed (sanity check only — no other file was touched):
   `cd backend && dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.FeatureFlags"`
   Expected: all tests in `HebloFeatureProviderTests`, `FeatureFlagRegistryFrontendMirrorTests`, `FeatureFlagsControllerLintTests`, and the new `ClearFlagOverrideHandlerTests` pass.

**Validation:**
- `cd backend && dotnet build` — must succeed with 0 errors/warnings introduced.
- `cd backend && dotnet format --verify-no-changes` — must report no formatting violations (or run `dotnet format` and confirm the diff is limited to the new test file).
- `cd backend && dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~ClearFlagOverrideHandlerTests"` — both `Handle_OverrideNotFound_ReturnsResourceNotFound_AndDoesNotTouchCache` and `Handle_OverrideDeleted_InvalidatesCache_AndReturnsSuccess` pass.
- `cd backend && dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.FeatureFlags"` — full FeatureFlags module test slice passes (no regressions).
- Coverage check (manual/CI): the new test file drives `ClearFlagOverrideHandler.Handle` through both of its branches, raising the handler's line coverage from 0% to effectively 100% of its own lines, satisfying the ≥60% threshold that triggered issue #4094.
