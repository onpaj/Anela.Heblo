### task: handler-identity-resolution

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/FeatureFlags/UseCases/UpsertFlagOverride/UpsertFlagOverrideRequest.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/FeatureFlags/UseCases/UpsertFlagOverride/UpsertFlagOverrideHandler.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/UpsertFlagOverrideHandlerTests.cs` (new file)

- [ ] **Step 1: Write the failing test file**

Create `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/UpsertFlagOverrideHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.FeatureFlags;
using Anela.Heblo.Application.Features.FeatureFlags.UseCases.UpsertFlagOverride;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.FeatureFlags;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.FeatureFlags;

public class UpsertFlagOverrideHandlerTests
{
    private readonly Mock<IFeatureFlagOverrideRepository> _repo = new();
    private readonly Mock<ICurrentUserService> _currentUserService = new();
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

    private UpsertFlagOverrideHandler CreateHandler() =>
        new(_repo.Object, _cache, _currentUserService.Object);

    [Fact]
    public async Task Handle_AuthenticatedUser_PersistsResolvedDisplayNameAsUpdatedBy()
    {
        _currentUserService.Setup(c => c.GetCurrentUser())
            .Returns(new CurrentUser("uid-1", "Jane Admin", "jane@example.com", IsAuthenticated: true));

        var response = await CreateHandler().Handle(
            new UpsertFlagOverrideRequest { Key = FeatureFlagKeys.LabelPrintingEnabled, IsEnabled = false },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        _repo.Verify(r => r.UpsertAsync(
            FeatureFlagKeys.LabelPrintingEnabled, false, "Jane Admin", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_UnauthenticatedCurrentUser_PersistsSystemFallback()
    {
        _currentUserService.Setup(c => c.GetCurrentUser())
            .Returns(new CurrentUser(null, null, null, IsAuthenticated: false));

        var response = await CreateHandler().Handle(
            new UpsertFlagOverrideRequest { Key = FeatureFlagKeys.LabelPrintingEnabled, IsEnabled = true },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        _repo.Verify(r => r.UpsertAsync(
            FeatureFlagKeys.LabelPrintingEnabled, true, "System", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_UnknownKey_ReturnsResourceNotFoundAndDoesNotCallRepo()
    {
        _currentUserService.Setup(c => c.GetCurrentUser())
            .Returns(new CurrentUser("uid-1", "Jane Admin", "jane@example.com", IsAuthenticated: true));

        var response = await CreateHandler().Handle(
            new UpsertFlagOverrideRequest { Key = "does-not-exist", IsEnabled = true },
            CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
        _repo.Verify(r => r.UpsertAsync(
            It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
```

- [ ] **Step 2: Run the test to confirm it fails (compile error — constructor shape doesn't match yet)**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UpsertFlagOverrideHandlerTests"`
Expected: build FAILS — `UpsertFlagOverrideHandler` has no constructor accepting `(IFeatureFlagOverrideRepository, IMemoryCache, ICurrentUserService)` and `UpsertFlagOverrideRequest` has no `UpdatedBy`-free-but-otherwise-matching shape mismatch is not yet the issue; the actual compile error will be about the 3-arg constructor call in `CreateHandler()` not matching the current 2-arg constructor. This is the expected failing state.

- [ ] **Step 3: Update `UpsertFlagOverrideRequest` — remove `UpdatedBy`**

Replace the full contents of `backend/src/Anela.Heblo.Application/Features/FeatureFlags/UseCases/UpsertFlagOverride/UpsertFlagOverrideRequest.cs` with:

```csharp
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.FeatureFlags.UseCases.UpsertFlagOverride;

public class UpsertFlagOverrideRequest : IRequest<UpsertFlagOverrideResponse>
{
    public string Key { get; init; } = "";
    public bool IsEnabled { get; init; }
}

public class UpsertFlagOverrideResponse : BaseResponse
{
    public UpsertFlagOverrideResponse() { }
    public UpsertFlagOverrideResponse(ErrorCodes errorCode) : base(errorCode) { }
}
```

- [ ] **Step 4: Update `UpsertFlagOverrideHandler` — inject `ICurrentUserService` and resolve identity in `Handle()`**

Replace the full contents of `backend/src/Anela.Heblo.Application/Features/FeatureFlags/UseCases/UpsertFlagOverride/UpsertFlagOverrideHandler.cs` with:

```csharp
using Anela.Heblo.Application.Features.FeatureFlags.Infrastructure;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Caching.Memory;

namespace Anela.Heblo.Application.Features.FeatureFlags.UseCases.UpsertFlagOverride;

internal sealed class UpsertFlagOverrideHandler : IRequestHandler<UpsertFlagOverrideRequest, UpsertFlagOverrideResponse>
{
    private readonly IFeatureFlagOverrideRepository _repo;
    private readonly IMemoryCache _cache;
    private readonly ICurrentUserService _currentUserService;

    public UpsertFlagOverrideHandler(
        IFeatureFlagOverrideRepository repo,
        IMemoryCache cache,
        ICurrentUserService currentUserService)
    {
        _repo = repo;
        _cache = cache;
        _currentUserService = currentUserService;
    }

    public async Task<UpsertFlagOverrideResponse> Handle(
        UpsertFlagOverrideRequest request, CancellationToken ct)
    {
        if (!FeatureFlagRegistry.ByKey.ContainsKey(request.Key))
            return new UpsertFlagOverrideResponse(ErrorCodes.ResourceNotFound);

        var updatedBy = _currentUserService.GetCurrentUser().GetDisplayName();
        await _repo.UpsertAsync(request.Key, request.IsEnabled, updatedBy, ct);
        _cache.Remove(HebloFeatureProvider.CacheKey);
        return new UpsertFlagOverrideResponse();
    }
}
```

Note the constructor parameter order changed (`cache` now precedes `currentUserService`) to match the test's `CreateHandler()` call above — keep both in sync exactly as written; do not reorder one without the other.

- [ ] **Step 5: Run the test to confirm it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UpsertFlagOverrideHandlerTests"`
Expected: PASS — 3 tests, 0 failures.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/FeatureFlags/UseCases/UpsertFlagOverride/UpsertFlagOverrideRequest.cs \
        backend/src/Anela.Heblo.Application/Features/FeatureFlags/UseCases/UpsertFlagOverride/UpsertFlagOverrideHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/FeatureFlags/UpsertFlagOverrideHandlerTests.cs
git commit -m "fix(feature-flags): resolve UpdatedBy in UpsertFlagOverrideHandler via ICurrentUserService"
```

---

