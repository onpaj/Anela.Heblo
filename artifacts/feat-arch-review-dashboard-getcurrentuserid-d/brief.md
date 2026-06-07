## Module
Dashboard

## Finding
The same six-line user-ID extraction method appears verbatim in at least three controllers:

- `DashboardController.cs:97–105`
- `CarrierCoolingController.cs:40–45`
- `GiftSettingsController.cs:40–45`

```csharp
private string GetCurrentUserId()
{
    return User.FindFirst(ClaimTypes.NameIdentifier)?.Value
               ?? User.FindFirst("sub")?.Value
               ?? User.FindFirst("oid")?.Value
               ?? throw new Exception("User not found");
}
```

`BaseApiController` already exists (`backend/src/Anela.Heblo.API/Controllers/BaseApiController.cs`) and is the established place for shared controller utilities (`HandleResponse<T>`, logger access). It does not expose `GetCurrentUserId`.

## Why it matters
- The claim fallback chain (`NameIdentifier` → `sub` → `oid`) encodes a non-obvious business rule (Azure AD Entra uses `oid`). If this rule changes, it must be updated in every copy independently.
- The bare `throw new Exception` instead of an `UnauthorizedAccessException` (or returning a 401) is inconsistent error handling — easy to accidentally fix in one copy and miss others.
- Violates DRY in infrastructure code that is explicitly shared via `BaseApiController`.

## Suggested fix
Add a single protected method to `BaseApiController`:

```csharp
protected string GetCurrentUserId()
    => User.FindFirst(ClaimTypes.NameIdentifier)?.Value
       ?? User.FindFirst("sub")?.Value
       ?? User.FindFirst("oid")?.Value
       ?? throw new UnauthorizedAccessException("Authenticated user has no identifiable claim.");
```

Remove the private copies from all three controllers (and any others that surface on a codebase-wide search).

---
_Filed by daily arch-review routine on 2026-05-28._