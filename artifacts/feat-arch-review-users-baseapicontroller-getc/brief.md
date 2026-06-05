## Module
Users

## Finding

`BaseApiController.GetCurrentUserId()` (`backend/src/Anela.Heblo.API/Controllers/BaseApiController.cs:75-79`) reimplements the identical claim-priority chain already owned by `CurrentUserService.GetCurrentUser()` (`backend/src/Anela.Heblo.API/Features/Users/CurrentUserService.cs:26-29`):

```csharp
// BaseApiController.cs:75-79
protected string GetCurrentUserId()
    => User.FindFirst(ClaimTypes.NameIdentifier)?.Value
       ?? User.FindFirst("sub")?.Value
       ?? User.FindFirst("oid")?.Value
       ?? throw new UnauthorizedAccessException("...");

// CurrentUserService.cs:26-29
var id = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value
         ?? user?.FindFirst("sub")?.Value
         ?? user?.FindFirst("oid")?.Value;
```

Three controllers call `GetCurrentUserId()` and bypass `ICurrentUserService` entirely:
- `DashboardController` — 5 call-sites (`backend/src/Anela.Heblo.API/Controllers/DashboardController.cs:37,47,57,71,85`)
- `CarrierCoolingController` — line 34 (`backend/src/Anela.Heblo.API/Controllers/CarrierCoolingController.cs:34`)
- `GiftSettingsController` — line 33 (`backend/src/Anela.Heblo.API/Controllers/GiftSettingsController.cs:33`)

All other handlers (Purchase, Logistics, InvoiceClassification, Article, Leaflet, Manufacture, etc.) inject `ICurrentUserService` and resolve user identity there. Two inconsistent patterns coexist for the same concern.

## Why it matters

**DRY violation with concrete maintenance cost:** The `oid` fallback was added to `CurrentUserService` (with a comment explaining the Entra ID guest-user scenario) but the same fallback is silently present in `BaseApiController`. If the chain changes again — a new claim source, a changed priority — both files must be updated and both are at risk of drifting. There is no test that verifies `BaseApiController.GetCurrentUserId()` handles the same edge cases as `CurrentUserService`.

**Inconsistent architecture:** `development_guidelines.md` requires that business logic lives in MediatR handlers, not controllers. The controllers that call `GetCurrentUserId()` and stamp it into a request field (`UserId`, `ModifiedBy`) are performing an identity-resolution concern that all other handlers perform inside the handler via `ICurrentUserService`. This makes the codebase harder to reason about — identical business intent expressed two different ways.

## Suggested fix

1. **Remove `GetCurrentUserId()` from `BaseApiController`** — there is no reason for this helper to exist once controllers use `ICurrentUserService`.

2. **For `DashboardController`:** The Dashboard handlers (`GetUserSettingsHandler`, `SaveUserSettingsHandler`, `GetTileDataHandler`, `EnableTileHandler`, `DisableTileHandler`) should inject `ICurrentUserService` and resolve the user ID themselves, consistent with Purchase/Logistics handlers.

3. **For `CarrierCoolingController` and `GiftSettingsController`:** The `SetCarrierCoolingHandler` and `SetGiftSettingCommandHandler` should inject `ICurrentUserService` and set `ModifiedBy` themselves, removing the need for controllers to touch it.

This unifies the pattern so user-identity resolution always lives in one place (`CurrentUserService`) accessed through one interface (`ICurrentUserService`), and all callers use it consistently.

---
_Filed by daily arch-review routine on 2026-06-04._