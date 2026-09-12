# Design: FeatureFlagsController identity resolution fix (ADR-005 compliance)

## Component Design

**`UpsertFlagOverrideHandler`** (`Anela.Heblo.Application.Features.FeatureFlags.UseCases.UpsertFlagOverride`)
- Responsibility: given a flag key and desired enabled state, validate the key exists, persist the override with the resolved acting-user identity, and invalidate the feature flag cache.
- New dependency: `ICurrentUserService` (`Anela.Heblo.Domain.Features.Users`), constructor-injected alongside the existing `IFeatureFlagOverrideRepository` and `IMemoryCache`.
- Interface contract (unchanged): `IRequestHandler<UpsertFlagOverrideRequest, UpsertFlagOverrideResponse>`.
- Internal behavior change only: the `updatedBy` value passed to `IFeatureFlagOverrideRepository.UpsertAsync` is now computed as `_currentUserService.GetCurrentUser().GetDisplayName()` instead of being read off the incoming request.

**`UpsertFlagOverrideRequest`** (`Anela.Heblo.Application.Features.FeatureFlags.UseCases.UpsertFlagOverride`)
- Responsibility: carry the two pieces of caller intent needed to perform the upsert — which flag, and the desired enabled state.
- Contract narrows: drops `UpdatedBy` (identity is no longer client/controller-supplied input to this request; it is the handler's own responsibility to resolve, per ADR-005).

**`FeatureFlagsController`** (`Anela.Heblo.API.Controllers`)
- Responsibility: unchanged — thin HTTP-to-MediatR dispatch for the feature-flags admin endpoints.
- `Put()` narrows to a pure mapping from route/body to `UpsertFlagOverrideRequest`, with no identity resolution, logging, or fallback logic — bringing it to parity with the controller's other three actions (`Get`, `GetAdmin`, `Delete`), which already contain zero business logic.

No new components are introduced; no existing component's public interface used by other modules changes (`IFeatureFlagOverrideRepository`, `UpsertFlagOverrideResponse`, `UpsertFlagOverrideBodyDto`, and the HTTP route/verb/attributes are all unchanged).

## Data Schemas

**HTTP contract — unchanged (no client/OpenAPI impact):**

`PUT /api/feature-flags/admin/{key}`
- Request body (`UpsertFlagOverrideBodyDto`): `{ "isEnabled": boolean }`
- Response (`UpsertFlagOverrideResponse`): `{ "success": boolean, "errorCode"?: string, ... }` (existing `BaseResponse` shape), 404 when `key` is not a registered flag.

**Internal MediatR request shape — changes:**

Before:
```csharp
public class UpsertFlagOverrideRequest : IRequest<UpsertFlagOverrideResponse>
{
    public string Key { get; init; } = "";
    public bool IsEnabled { get; init; }
    public string UpdatedBy { get; init; } = "";
}
```

After:
```csharp
public class UpsertFlagOverrideRequest : IRequest<UpsertFlagOverrideResponse>
{
    public string Key { get; init; } = "";
    public bool IsEnabled { get; init; }
}
```

**Persisted data — unchanged shape, changed source:** `IFeatureFlagOverrideRepository.UpsertAsync(string key, bool isEnabled, string updatedBy, CancellationToken ct)` keeps its signature and persisted `updatedBy` string column/field as-is. Only the value supplied for `updatedBy` changes source (handler-resolved via `ICurrentUserService.GetCurrentUser().GetDisplayName()` instead of controller-stamped via `User.Identity?.Name`), and the fallback literal for an unresolvable identity changes from `"unknown"` to the canonical `"System"` (per `CurrentUserExtensions.GetDisplayName()`).
