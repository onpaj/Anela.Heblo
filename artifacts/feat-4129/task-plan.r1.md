# FeatureFlagsController Identity Resolution Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move `UpdatedBy` identity resolution for feature-flag overrides out of `FeatureFlagsController` and into `UpsertFlagOverrideHandler` via `ICurrentUserService`, per ADR-005.

**Architecture:** `UpsertFlagOverrideRequest` drops its `UpdatedBy` property; `UpsertFlagOverrideHandler` gains an `ICurrentUserService` dependency and resolves `_currentUserService.GetCurrentUser().GetDisplayName()` internally; `FeatureFlagsController.Put()` becomes a plain dispatcher like the controller's other three actions. No HTTP contract, persistence schema, or DI registration changes.

**Tech Stack:** .NET 8, MediatR, xUnit, Moq, FluentAssertions.

---

## Task decomposition rationale

This is a single tightly-coupled unit of change across three files (`UpsertFlagOverrideRequest`, `UpsertFlagOverrideHandler`, `FeatureFlagsController`) — `UpsertFlagOverrideRequest.UpdatedBy` and its two call sites cannot be split into independently-shippable increments without an intermediate broken-compile state. It is decomposed into two tasks along the natural seam: (1) the Application-layer change (request DTO + handler + new handler unit test, which is fully testable in isolation via mocks), and (2) the API-layer change (controller simplification), which depends on task 1's request shape and is verified by the existing controller lint test plus a manual/build check (no controller-level unit test exists in this codebase for `FeatureFlagsController`, consistent with its siblings — `Get`/`GetAdmin`/`Delete` have no dedicated unit tests either, only the reflection-based `FeatureFlagsControllerLintTests`).

---

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

### task: controller-simplification

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/FeatureFlagsController.cs`

- [ ] **Step 1: Confirm the existing controller lint test still targets `Put` correctly (no change expected, run before editing as a baseline)**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~FeatureFlagsControllerLintTests"`
Expected: PASS (this test uses reflection over method attributes, unaffected by the body changes below — this step is a baseline confirmation, not new coverage).

- [ ] **Step 2: Simplify `Put()` to a straight-through dispatcher**

In `backend/src/Anela.Heblo.API/Controllers/FeatureFlagsController.cs`, replace the `Put` method body:

```csharp
    [HttpPut("admin/{key}")]
    [FeatureAuthorize(Feature.Admin_FeatureFlags, AccessLevel.Write)]
    [ProducesResponseType(typeof(UpsertFlagOverrideResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(UpsertFlagOverrideResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<UpsertFlagOverrideResponse>> Put(
        string key,
        [FromBody] UpsertFlagOverrideBodyDto body,
        CancellationToken ct)
        => HandleResponse(await _mediator.Send(new UpsertFlagOverrideRequest
        {
            Key = key,
            IsEnabled = body.IsEnabled,
        }, ct));
```

This removes the `var name = User.Identity?.Name;` / `Logger.LogWarning(...)` / `var updatedBy = name ?? "unknown";` lines entirely, matching the expression-bodied dispatcher style already used by `Get`, `GetAdmin`, and `Delete` in this same file.

- [ ] **Step 3: Run the full FeatureFlags test suite**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~FeatureFlags"`
Expected: PASS — all tests under `Anela.Heblo.Tests.Features.FeatureFlags` (including `FeatureFlagsControllerLintTests`, `FeatureFlagRegistryFrontendMirrorTests`, and the new `UpsertFlagOverrideHandlerTests` from task 1) pass with 0 failures.

- [ ] **Step 4: Full backend build and format check**

Run: `cd backend && dotnet build`
Expected: Build succeeds, 0 errors (confirms no other reference to the removed `UpdatedBy` property or the old 2-arg handler constructor was missed).

Run: `cd backend && dotnet format --verify-no-changes`
Expected: No formatting violations. If it reports changes, run `dotnet format` and re-verify.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/FeatureFlagsController.cs
git commit -m "fix(feature-flags): simplify FeatureFlagsController.Put to a straight-through dispatcher"
```

---

## Self-review notes

**Spec coverage:**
- FR-1 (resolve `UpdatedBy` inside the handler) → `task: handler-identity-resolution`, Step 4.
- FR-2 (remove `UpdatedBy` from the request) → `task: handler-identity-resolution`, Step 3.
- FR-3 (simplify the controller) → `task: controller-simplification`, Step 2.
- FR-4 (preserve behavior for the authenticated case; document the `"unknown"` → `"System"` fallback change) → covered by `UpsertFlagOverrideHandlerTests.Handle_AuthenticatedUser_PersistsResolvedDisplayNameAsUpdatedBy` and `Handle_UnauthenticatedCurrentUser_PersistsSystemFallback` respectively.
- NFR-1/NFR-2 — no code artifact required (architectural properties, not testable behavior); satisfied by construction since `ICurrentUserService` is the same shared, already-audited implementation every other handler uses.

**Placeholder scan:** none — every step includes complete, runnable code and exact commands with expected output.

**Type consistency:** `UpsertFlagOverrideHandler`'s constructor parameter order (`repo, cache, currentUserService`) is used identically in the plan's test (`CreateHandler()`) and in the handler implementation (Step 4) — verified consistent. `FeatureFlagKeys.LabelPrintingEnabled` (existing registry entry, confirmed present in `FeatureFlagRegistry.cs`) is used as the "valid key" fixture across all three new test cases instead of inventing an unregistered constant.
