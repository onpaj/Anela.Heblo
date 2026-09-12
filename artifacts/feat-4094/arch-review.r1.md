# Architecture Review: Unit Tests for ClearFlagOverrideHandler

## Skip Design: true

## Architectural Fit Assessment
This is a pure coverage-gap fix with zero production-code risk: it adds one new xUnit test class to the existing `Anela.Heblo.Tests` project, mirroring the source tree under `Features/FeatureFlags/UseCases/ClearFlagOverride/`. The pattern is already fully established in this codebase — `GetCalendarViewHandlerTests.cs` (`Features/Manufacture/UseCases/GetCalendarView/`) is the canonical "constructor-injects-mocks, one `[Fact]` per branch" shape for a MediatR handler test, and `HebloFeatureProviderTests.cs` (same FeatureFlags module) is the sibling proof that `HebloFeatureProvider.CacheKey` (an `internal const string` on an `internal sealed class`) is already reachable from the test project — `InternalsVisibleTo("Anela.Heblo.Tests")` is declared in `backend/src/Anela.Heblo.Application/AssemblyInfo.cs`, so no project/assembly configuration work is needed.

`ClearFlagOverrideHandler` itself is a minimal two-branch MediatR handler (`internal sealed`, constructor-injecting `IFeatureFlagOverrideRepository` and `IMemoryCache`), and `ClearFlagOverrideRequest`/`ClearFlagOverrideResponse` are plain public classes (not records — consistent with the project's DTO rule) living in the same file. There is no controller, validator, or DI wiring in scope. The only integration points a test needs to touch are: `IFeatureFlagOverrideRepository.DeleteAsync(string, CancellationToken)` (mocked) and `IMemoryCache.Remove(object)` (mocked directly — it's a real interface member, not an extension method, so Moq can set up/verify it without any wrapper).

Note in passing (no action needed, out of scope): `UpsertFlagOverrideHandler` — the closest sibling handler in the same folder tree, with the identical `_cache.Remove(HebloFeatureProvider.CacheKey)` cache-invalidation call — also has no test file today. It is not part of this task's scope, but the same test shape proposed here would apply to it if a future coverage-gap pass targets it.

## Proposed Architecture

### Component Overview
```
ClearFlagOverrideHandlerTests (new)
  ├─ Mock<IFeatureFlagOverrideRepository>  -- stubs DeleteAsync(key, ct) -> bool
  ├─ Mock<IMemoryCache>                    -- verifies Remove(object key)
  └─ ClearFlagOverrideHandler (real, under test)
         .Handle(ClearFlagOverrideRequest, CancellationToken)
              ├─ not-found path:  DeleteAsync -> false  =>  ResourceNotFound response, cache untouched
              └─ deleted path:    DeleteAsync -> true   =>  cache.Remove(HebloFeatureProvider.CacheKey), success response
```
No new components, no DI container involvement — direct constructor injection of mocks into the handler, exactly like `GetCalendarViewHandlerTests`.

### Key Design Decisions

#### Decision 1: Test class shape and location
**Chosen approach:** Create `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/UseCases/ClearFlagOverride/ClearFlagOverrideHandlerTests.cs`, namespace `Anela.Heblo.Tests.Features.FeatureFlags.UseCases.ClearFlagOverride`. Two private readonly mock fields (`Mock<IFeatureFlagOverrideRepository> _repoMock`, `Mock<IMemoryCache> _cacheMock`) plus a `ClearFlagOverrideHandler _handler` field, all built in the constructor — matching `GetCalendarViewHandlerTests` field/constructor layout exactly (not the ad-hoc `MakeProvider()` factory-method style used in `HebloFeatureProviderTests`, which exists there only because the SUT needs a `ServiceCollection`/`IServiceScopeFactory` this handler does not use).
**Rationale:** This handler takes two constructor dependencies with no setup complexity, so the simpler "wire once in the constructor" pattern used for other lightweight CQRS handlers in this suite is the right fit — introducing a factory method here would be inventing a new convention for no benefit.

#### Decision 2: Mocking `IMemoryCache.Remove`
**Chosen approach:** Mock `IMemoryCache` directly with Moq and verify `_cacheMock.Verify(c => c.Remove(HebloFeatureProvider.CacheKey), Times.Once)` on the success path and `_cacheMock.Verify(c => c.Remove(It.IsAny<object>()), Times.Never)` on the not-found path. No `CreateEntry`/`TryGetValue` setup, no real `MemoryCache` instance.
**Rationale:** `IMemoryCache.Remove(object key)` is a genuine interface method (unlike some cache extension helpers), so Moq can mock/verify it with no wrapper or adapter. This is more precise than instantiating a real `MemoryCache` (as `HebloFeatureProviderTests` does for its own, different purpose of testing actual cache TTL behavior) — here the test only needs to assert the *call*, not real caching semantics, and asserting the call with the exact key value directly guards the regression this task exists to catch (a broken `HebloFeatureProvider.CacheKey` string match).

#### Decision 3: Assertions on the response and the repository call
**Chosen approach:** Use FluentAssertions (`result.Success.Should().BeTrue()/BeFalse()`, `result.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound)` / `.Should().BeNull()`), and verify the repository call with `_repoMock.Verify(r => r.DeleteAsync(request.Key, It.IsAny<CancellationToken>()), Times.Once)` in both tests.
**Rationale:** Matches the assertion style used throughout `GetCalendarViewHandlerTests` (`result.Success.Should().BeTrue()`, `result.ErrorCode.Should().Be(ErrorCodes.InternalServerError)`) and keeps the request's `Key` value visibly threaded through to the repository call, which the spec's FR-3 calls out as a required guard against a hardcoded/dropped key.

## Implementation Guidance

### Directory / Module Structure
Single new file:
`backend/test/Anela.Heblo.Tests/Features/FeatureFlags/UseCases/ClearFlagOverride/ClearFlagOverrideHandlerTests.cs`

No other files change. No new test helper/fixture classes are needed — the handler has no shared setup complex enough to warrant one.

### Interfaces and Contracts
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
This is not "extra" code to design around — it is the deliverable. No production interface changes accompany it.

### Data Flow
Test → `Mock<IFeatureFlagOverrideRepository>.Setup(DeleteAsync)` returns a canned `bool` → `_handler.Handle(request, ct)` runs the real handler logic → handler branches on the mocked return value → either builds `new ClearFlagOverrideResponse(ErrorCodes.ResourceNotFound)` without touching `_cache`, or calls `_cache.Remove(HebloFeatureProvider.CacheKey)` and returns `new ClearFlagOverrideResponse()` → test asserts on the returned response object and on the mock invocation history via `Verify`. No database, no HTTP, no real cache — everything is in-process and synchronous-equivalent (`Task`-wrapped), consistent with NFR-1 (sub-second unit test).

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| `IMemoryCache.Remove(object)` overload ambiguity trips up Moq's expression matching | Low | Verify with the exact `string` constant (`HebloFeatureProvider.CacheKey`), which implicitly matches the `object` parameter; this is the same approach already implied by the spec and requires no extension-method shims. |
| Test asserts on `internal` type (`HebloFeatureProvider`) without `InternalsVisibleTo` in place | Low (already ruled out) | Confirmed present in `backend/src/Anela.Heblo.Application/AssemblyInfo.cs` (`InternalsVisibleTo("Anela.Heblo.Tests")`) and already exercised successfully by `HebloFeatureProviderTests.cs` in the same assembly pair. |
| Future refactor removes/renames `HebloFeatureProvider.CacheKey` silently | Medium (this is exactly the regression the brief targets) | Test references the constant symbolically (`HebloFeatureProvider.CacheKey`), not a hardcoded string literal, so a rename is a compile error in the test and a behavior change is caught by the `Times.Once`/`Times.Never` verification — no test-side hardcoding to keep in sync. |

## Specification Amendments
None. The spec's class shape, file path, namespace, test names, and mocking approach are already fully aligned with real, verified conventions in this repo (`GetCalendarViewHandlerTests` for structure, `HebloFeatureProviderTests` for `InternalsVisibleTo`/`HebloFeatureProvider` accessibility). Implement as specified.

## Prerequisites
None. All referenced types (`ClearFlagOverrideHandler`, `ClearFlagOverrideRequest`, `ClearFlagOverrideResponse`, `IFeatureFlagOverrideRepository`, `HebloFeatureProvider.CacheKey`, `ErrorCodes.ResourceNotFound`) already exist and compile today; `InternalsVisibleTo` is already configured; xUnit/Moq/FluentAssertions are already referenced by `Anela.Heblo.Tests`. Implementation can start immediately by adding the single test file above.
