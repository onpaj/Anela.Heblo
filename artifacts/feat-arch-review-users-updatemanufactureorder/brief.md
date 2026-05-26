## Module
Users (violation in Manufacture module)

## Finding
`UpdateManufactureOrderStatusHandler` injects `IHttpContextAccessor` directly and resolves the current user itself, duplicating and diverging from the existing `ICurrentUserService` abstraction.

```csharp
// backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrderStatus/UpdateManufactureOrderStatusHandler.cs, lines 17–24, 169–173
private readonly IHttpContextAccessor _httpContextAccessor;

public UpdateManufactureOrderStatusHandler(
    ...
    IHttpContextAccessor httpContextAccessor,
    ...)

private string GetCurrentUserName()
{
    var user = _httpContextAccessor.HttpContext?.User;
    return user?.Identity?.Name ?? "System";
}
```

`ICurrentUserService.GetCurrentUser()` (in `CurrentUserService.cs`) applies a chain of Entra ID claim fallbacks (`preferred_username`, `upn`, `oid`, `sub`, …). This handler reads only `Identity.Name` with no fallbacks, so in Entra ID access-token flows where `Name` is absent the stored `StateChangedByUser` will be `"System"` even for authenticated users.

## Why it matters
- Bypasses the `ICurrentUserService` abstraction that was introduced specifically to centralise Entra ID claim resolution, producing inconsistent audit trail entries.
- Adds a second direct `IHttpContextAccessor` consumer in the Application layer, worsening the coupling identified in #1716.
- The handler has an additional responsibility (HTTP identity extraction) that violates Single Responsibility.

## Suggested fix
Replace the `IHttpContextAccessor` dependency with `ICurrentUserService`:

```csharp
// Remove: private readonly IHttpContextAccessor _httpContextAccessor;
private readonly ICurrentUserService _currentUserService;

// In constructor: replace httpContextAccessor parameter with ICurrentUserService currentUserService
// Remove GetCurrentUserName() and replace its two call-sites with:
_currentUserService.GetCurrentUser().GetDisplayName()
// (using the existing CurrentUserExtensions.GetDisplayName())
```

---
_Filed by daily arch-review routine on 2026-05-26._