All 10 tasks complete. Writing the output summary.

---

# Implementation: Consolidate User Identity Resolution via `ICurrentUserService`

## What was implemented

Removed the controller-level `BaseApiController.GetCurrentUserId()` helper and migrated all three outlier controllers (`DashboardController`, `CarrierCoolingController`, `GiftSettingsController`) so user identity is resolved exclusively in MediatR handlers via `ICurrentUserService` — matching the established pattern used by 60+ other handlers.

## Files created/modified

**Backend — Application handlers (7 files):**
- `backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/SetCarrierCooling/SetCarrierCoolingHandler.cs` — inject `ICurrentUserService`, early-return `Unauthorized` when id null/empty
- `backend/src/Anela.Heblo.Application/Features/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingHandler.cs` — same pattern
- `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/GetUserSettings/GetUserSettingsHandler.cs` — inject service, `"anonymous"` fallback
- `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/SaveUserSettings/SaveUserSettingsHandler.cs` — same
- `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/GetTileData/GetTileDataHandler.cs` — inject service (delegates to GetUserSettings internally)
- `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/EnableTile/EnableTileHandler.cs` — inject service, pass resolved userId to mutator
- `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/DisableTile/DisableTileHandler.cs` — same

**Backend — Request DTOs (7 files, each lost one property):**
- `SetCarrierCoolingRequest.cs` — removed `ModifiedBy`
- `SetGiftSettingCommand.cs` — removed `ModifiedBy`
- `GetUserSettingsRequest.cs`, `SaveUserSettingsRequest.cs`, `GetTileDataRequest.cs`, `EnableTileRequest.cs`, `DisableTileRequest.cs` — all removed `UserId`

**Backend — Controllers:**
- `BaseApiController.cs` — removed `GetCurrentUserId()` method and `using System.Security.Claims;`
- `DashboardController.cs` — removed all 5 `GetCurrentUserId()` / `UserId=` assignments
- `CarrierCoolingController.cs` — removed `request.ModifiedBy = GetCurrentUserId();`
- `GiftSettingsController.cs` — removed `command.ModifiedBy = GetCurrentUserId();`, added explicit `Unauthorized` branch

**Backend — Infrastructure:**
- `UserDashboardSettingsMutator.cs` — patched internal `GetUserSettingsRequest` sends (removed `UserId` initializer)

**Tests:**
- `backend/test/Anela.Heblo.Tests/Controllers/BaseApiControllerTests.cs` — **DELETED**
- `backend/test/Anela.Heblo.Tests/Features/Users/CurrentUserServiceTests.cs` — added 3 priority-chain tests (total 11)
- `SetCarrierCoolingHandlerTests.cs`, `SetGiftSettingHandlerTests.cs` — inject mocked `ICurrentUserService`, added `Unauthorized` test
- All 5 Dashboard handler tests — updated to inject mocked `ICurrentUserService`
- `DashboardControllerTests.cs` — removed stale `UserId` assertions

## Tests

- `CurrentUserServiceTests` — 11 tests (8 existing + 3 new priority-chain scenarios)
- `SetCarrierCoolingHandlerTests` — 4 tests including new Unauthorized test
- `SetGiftSettingHandlerTests` — 6 tests including new Unauthorized test
- `GetUserSettingsHandlerTests` — 7 tests with mock-based anonymous fallback testing
- `SaveUserSettingsHandlerTests` — 8 tests
- `GetTileDataHandlerTests` — 8 tests
- `EnableTileHandlerTests` — 7 tests
- `DisableTileHandlerTests` — 7 tests
- All 4372 backend unit tests pass

## How to verify

```bash
# Build and test backend
dotnet build backend/Anela.Heblo.sln
dotnet test backend/Anela.Heblo.sln

# Verify no remaining call-sites
grep -rn "GetCurrentUserId" backend/src backend/test  # should be zero

# Frontend build/lint
cd frontend && npm run build && npm run lint
```

## Notes

- `ICurrentUserService` lifetime is Singleton (not Scoped) — intentional, `IHttpContextAccessor` is AsyncLocal-backed.
- `UserDashboardSettingsMutator` keeps its existing `userId` parameter and `"anonymous"` normalization — handler resolves identity, mutator normalizes.
- `GiftSettingsController` retains its `NoContent()` (204) success contract; explicit `Unauthorized` branch added before `BadRequest`.
- Pre-existing lint warnings (144 in test files) are unrelated to this change.

## PR Summary

Removes `BaseApiController.GetCurrentUserId()` — a duplicated claim-resolution helper that mirrored `CurrentUserService.GetCurrentUser()` — and migrates the three outlier controllers (`Dashboard`, `CarrierCooling`, `GiftSettings`) to the canonical handler-side identity resolution pattern already used by 60+ other handlers. Identity resolution now lives in a single place: `ICurrentUserService`, injected into handlers.

The `UserId` / `ModifiedBy` fields on seven request DTOs are removed, closing a theoretical spoofing hole and making the API contract explicit. `SetCarrierCoolingHandler` and `SetGiftSettingHandler` return `ErrorCodes.Unauthorized` (HTTP 401) when `CurrentUser.Id` is null, matching the `CreateMarketingActionHandler` pattern. Dashboard handlers retain the existing `"anonymous"` defense-in-depth fallback.

### Changes
- `BaseApiController.cs` — removed `GetCurrentUserId()` helper and `using System.Security.Claims`
- 7 handler files — added `ICurrentUserService` constructor injection with inline resolution
- 7 request DTO files — removed client-settable `UserId` / `ModifiedBy` properties
- 3 controller files — removed controller-side identity assignments
- `GiftSettingsController.cs` — added explicit `Unauthorized` branch before `BadRequest`
- `BaseApiControllerTests.cs` — deleted; scenarios moved to `CurrentUserServiceTests.cs`
- `CurrentUserServiceTests.cs` — 3 new claim priority-chain tests added

## Status
DONE