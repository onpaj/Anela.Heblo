# Design: Unit Tests for ClearFlagOverrideHandler

## Component Design

**New component:** `ClearFlagOverrideHandlerTests` (xUnit test class)
Location: `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/UseCases/ClearFlagOverride/ClearFlagOverrideHandlerTests.cs`
Namespace: `Anela.Heblo.Tests.Features.FeatureFlags.UseCases.ClearFlagOverride`

**Responsibility:** Exercise both execution branches of `ClearFlagOverrideHandler.Handle(ClearFlagOverrideRequest, CancellationToken)` in isolation, with both of its dependencies replaced by mocks, so that neither the not-found short-circuit nor the cache-invalidation-on-delete behavior can regress silently.

**Structure (matches `GetCalendarViewHandlerTests` convention — constructor-wired mocks, no factory method):**
- Fields: `Mock<IFeatureFlagOverrideRepository> _repoMock`, `Mock<IMemoryCache> _cacheMock`, `ClearFlagOverrideHandler _handler` (all `readonly`).
- Constructor: instantiates the two mocks and builds `_handler = new ClearFlagOverrideHandler(_repoMock.Object, _cacheMock.Object)` — no `ServiceCollection`/DI container involved, since the handler takes only these two constructor dependencies directly.
- Two `[Fact]` test methods, one per branch:
  - `Handle_OverrideNotFound_ReturnsResourceNotFound_AndDoesNotTouchCache` — drives the "repository reports no row deleted" branch.
  - `Handle_OverrideDeleted_InvalidatesCache_AndReturnsSuccess` — drives the "repository reports a row deleted" branch.

**Relationship to the handler under test:**
`ClearFlagOverrideHandlerTests` treats `ClearFlagOverrideHandler` as a black box invoked through its public `Handle` method. It supplies canned `bool` return values from the mocked `IFeatureFlagOverrideRepository.DeleteAsync`, lets the real (unmodified) handler logic run synchronously-equivalent (`Task`-wrapped, no I/O), and then inspects two observable surfaces:
1. the `ClearFlagOverrideResponse` returned by `Handle`, via FluentAssertions (`Success`, `ErrorCode`);
2. the mock invocation history, via Moq `Verify` calls on both `_repoMock` (was `DeleteAsync` called once, with the request's `Key` and a `CancellationToken`) and `_cacheMock` (was `Remove` called, or explicitly not called, with the expected key).

No other component is introduced. No production code changes accompany this test class — `ClearFlagOverrideHandler`, `ClearFlagOverrideRequest`, `ClearFlagOverrideResponse`, `IFeatureFlagOverrideRepository`, and `HebloFeatureProvider.CacheKey` are all consumed as-is.

## Data Schemas

**Request shape consumed by the handler (existing, unchanged):**
```csharp
public class ClearFlagOverrideRequest
{
    string Key;
}
```
Both tests construct it as `new ClearFlagOverrideRequest { Key = "some-flag-key" }` — an arbitrary non-empty string; the handler treats `Key` opaquely so no specific `FeatureFlagKeys` value is required.

**Response shape returned by the handler (existing, unchanged):**
```csharp
public class ClearFlagOverrideResponse : BaseResponse
{
    bool Success;
    string? ErrorCode;   // e.g. ErrorCodes.ResourceNotFound, or null on success
    object? Params;
}
```
- Not-found path: `Success == false`, `ErrorCode == ErrorCodes.ResourceNotFound`.
- Success path: `Success == true`, `ErrorCode == null`.

**Mock setup — `IFeatureFlagOverrideRepository.DeleteAsync`:**
```csharp
_repoMock
    .Setup(r => r.DeleteAsync(request.Key, It.IsAny<CancellationToken>()))
    .ReturnsAsync(false);   // not-found test
// or
    .ReturnsAsync(true);    // success test
```
Verified in both tests:
```csharp
_repoMock.Verify(r => r.DeleteAsync(request.Key, It.IsAny<CancellationToken>()), Times.Once);
```

**Mock setup/verification — `IMemoryCache.Remove`:**
`IMemoryCache.Remove(object key)` is a direct interface member (not an extension method), so Moq verifies it without a wrapper. No `Setup` is required for this task's assertions — only `Verify`:
- Not-found path: `_cacheMock.Verify(c => c.Remove(It.IsAny<object>()), Times.Never);`
- Success path: `_cacheMock.Verify(c => c.Remove(HebloFeatureProvider.CacheKey), Times.Once);` — asserted against the symbolic `internal const string` constant (`"feature_flag_overrides"`), not a hardcoded literal, so a future rename of the constant fails compilation rather than silently desyncing the test.

No database schema, API contract, or event payload is introduced or changed by this work.
