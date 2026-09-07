# Specification: Unit Tests for ClearFlagOverrideHandler

## Summary
`ClearFlagOverrideHandler` (Feature Flags module) currently has 0% line coverage against a 60% threshold. This spec adds a focused xUnit test class covering the handler's two execution paths — override-not-found and successful-delete-with-cache-invalidation — using Moq for both of its dependencies, `IFeatureFlagOverrideRepository` and `IMemoryCache`.

## Background
`ClearFlagOverrideHandler` is the MediatR handler behind the "remove a feature-flag override" use case. It deletes an override row via `IFeatureFlagOverrideRepository.DeleteAsync` and, only when a row was actually deleted, invalidates the in-memory cache entry (`HebloFeatureProvider.CacheKey`) that `HebloFeatureProvider` uses to cache DB overrides for 30 seconds (see `HebloFeatureProvider.GetOverridesAsync`). No test file exists for this handler today, so neither the cache-invalidation-on-delete behavior nor the not-found short-circuit is asserted anywhere in the suite. A silent regression here (e.g. a refactor of `HebloFeatureProvider.CacheKey` that breaks the string match) would let a deleted override keep forcing its old value for up to 30 seconds after deletion, with no test catching it. This is a pure test-coverage gap — no production code changes are in scope.

## Functional Requirements

### FR-1: Test not-found path — no cache mutation
Add a test that arranges `IFeatureFlagOverrideRepository.DeleteAsync(request.Key, ct)` to return `false` (simulating no matching override row), invokes `ClearFlagOverrideHandler.Handle`, and asserts:
- The returned `ClearFlagOverrideResponse.Success` is `false`.
- `ClearFlagOverrideResponse.ErrorCode` equals `ErrorCodes.ResourceNotFound`.
- `IMemoryCache.Remove` is never invoked (`Times.Never`).

**Acceptance criteria:**
- Test method name: `Handle_OverrideNotFound_ReturnsResourceNotFound_AndDoesNotTouchCache`.
- Test fails if the handler's early-return branch is removed or its error code changes.
- Test fails if `_cache.Remove` is called on this path (guards the "must NOT touch cache when missing" invariant called out in the brief).

### FR-2: Test successful-delete path — cache invalidated
Add a test that arranges `DeleteAsync` to return `true`, invokes `Handle`, and asserts:
- The returned response's `Success` is `true` and `ErrorCode` is `null`.
- `IMemoryCache.Remove` is called exactly once with the key `HebloFeatureProvider.CacheKey` (i.e. `"feature_flag_overrides"`).

**Acceptance criteria:**
- Test method name: `Handle_OverrideDeleted_InvalidatesCache_AndReturnsSuccess`.
- Test fails if the `_cache.Remove(HebloFeatureProvider.CacheKey)` call is dropped from the handler (the regression scenario the brief is concerned about).
- Test fails if the wrong cache key is passed (mocking `IMemoryCache.Remove(object)` directly, per the mocking approach below, makes the exact key value assertable).

### FR-3: Verify request key is passed through to the repository
Both tests (or a dedicated third test) must assert that `DeleteAsync` is invoked with the same `Key` value that was set on the `ClearFlagOverrideRequest`, and with a cancellation token, to guard against the key being dropped or hardcoded.

**Acceptance criteria:**
- `_repoMock.Verify(r => r.DeleteAsync(request.Key, It.IsAny<CancellationToken>()), Times.Once)` (or equivalent) is asserted in each test.

## Non-Functional Requirements

### NFR-1: Performance
Not applicable. This is a pure unit-test addition with mocked dependencies (no I/O); no performance targets apply. Tests must run in well under a second, consistent with the rest of the unit-test suite.

### NFR-2: Security
Not applicable. No auth, secrets, or sensitive data are involved — the handler and its tests operate only on a feature-flag key string and a boolean deletion result.

## Data Model
No new data model. Relevant existing types (all already present in the repo, described here for reference only — no changes needed):
- `ClearFlagOverrideRequest { string Key }` — MediatR request (`Anela.Heblo.Application.Features.FeatureFlags.UseCases.ClearFlagOverride`).
- `ClearFlagOverrideResponse : BaseResponse` — has a parameterless (success) constructor and an `ErrorCodes` constructor (failure); exposes `Success`, `ErrorCode`, `Params`.
- `IFeatureFlagOverrideRepository.DeleteAsync(string key, CancellationToken ct = default) : Task<bool>` (defined in `Anela.Heblo.Domain.Features.FeatureFlags`, re-exported via global `using` alias in the Application layer).
- `HebloFeatureProvider.CacheKey` — internal `const string` = `"feature_flag_overrides"`, accessible to the test project since it's `internal` and the Application assembly must expose `InternalsVisibleTo` the test project (already relied upon by `ClearFlagOverrideHandler` itself, which is also `internal sealed`; confirm the test project can reference/instantiate it — see Open Questions).

## API / Interface Design
No new API surface. This work only adds tests for the existing internal MediatR handler; no controller, DTO, or endpoint changes.

Test file to create:
`backend/test/Anela.Heblo.Tests/Features/FeatureFlags/UseCases/ClearFlagOverride/ClearFlagOverrideHandlerTests.cs`

This mirrors the source layout (`backend/src/Anela.Heblo.Application/Features/FeatureFlags/UseCases/ClearFlagOverride/ClearFlagOverrideHandler.cs`) and follows the existing convention used elsewhere in the suite (e.g. `Features/Manufacture/UseCases/GetCalendarView/GetCalendarViewHandlerTests.cs`), rather than being placed flat alongside `HebloFeatureProviderTests.cs`.

Namespace: `Anela.Heblo.Tests.Features.FeatureFlags.UseCases.ClearFlagOverride`

Class shape:
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
    private readonly Mock<IFeatureFlagOverrideRepository> _repoMock = new();
    private readonly Mock<IMemoryCache> _cacheMock = new();
    private readonly ClearFlagOverrideHandler _handler;

    public ClearFlagOverrideHandlerTests()
    {
        _handler = new ClearFlagOverrideHandler(_repoMock.Object, _cacheMock.Object);
    }

    [Fact]
    public async Task Handle_OverrideNotFound_ReturnsResourceNotFound_AndDoesNotTouchCache() { ... }

    [Fact]
    public async Task Handle_OverrideDeleted_InvalidatesCache_AndReturnsSuccess() { ... }
}
```

Mocking approach:
- `IFeatureFlagOverrideRepository`: `Mock<IFeatureFlagOverrideRepository>`, stub `DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())` to `ReturnsAsync(false)` / `ReturnsAsync(true)` per test; verify with `_repoMock.Verify(r => r.DeleteAsync(request.Key, It.IsAny<CancellationToken>()), Times.Once)`.
- `IMemoryCache`: `Mock<IMemoryCache>`. `IMemoryCache.Remove(object key)` is a regular interface method (not an extension), so it can be mocked and verified directly: `_cacheMock.Verify(c => c.Remove(HebloFeatureProvider.CacheKey), Times.Once)` for the success path and `_cacheMock.Verify(c => c.Remove(It.IsAny<object>()), Times.Never)` for the not-found path. No `CreateEntry`/`TryGetValue` setup is needed since the handler only calls `Remove`.
- Both request objects use `new ClearFlagOverrideRequest { Key = "some-flag-key" }` (an arbitrary non-empty string is sufficient; no specific key from `FeatureFlagKeys` needs to be used since the handler treats `Key` opaquely).

## Dependencies
- Existing test project `backend/test/Anela.Heblo.Tests` (xUnit, Moq, FluentAssertions — already used identically in `HebloFeatureProviderTests.cs`, in the same module).
- `HebloFeatureProvider.CacheKey` must be accessible from the test project. It's declared `internal const string` on an `internal sealed class` in `Anela.Heblo.Application`; `HebloFeatureProviderTests.cs` already references the enclosing type from the test project successfully, confirming `InternalsVisibleTo` is already configured for this assembly pair — no new dependency/configuration needed.
- No changes to `ClearFlagOverrideHandler.cs`, `ClearFlagOverrideRequest.cs`, `IFeatureFlagOverrideRepository.cs`, or `HebloFeatureProvider.cs` are required or in scope.

## Out of Scope
- Any modification to `ClearFlagOverrideHandler` or its production behavior.
- Integration/E2E tests exercising the real `IFeatureFlagOverrideRepository` (e.g. EF Core-backed) or the real `IMemoryCache` end-to-end through `HebloFeatureProvider`.
- Testing `HebloFeatureProvider` itself (already covered by `HebloFeatureProviderTests.cs`).
- Testing the controller/endpoint that dispatches `ClearFlagOverrideRequest` (not part of the identified coverage gap).
- Raising or changing the repo's global coverage threshold configuration.

## Open Questions
None.

## Status: COMPLETE
