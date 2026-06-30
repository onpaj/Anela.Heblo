## Module
UserManagement

## Finding
`GetEntraAccessUsersHandler` in the `Authorization` module directly injects and uses `IGraphService` from the `UserManagement` module's `Services` namespace:

```csharp
// backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetEntraAccessUsers/GetEntraAccessUsersHandler.cs
using Anela.Heblo.Application.Features.UserManagement.Services;  // line 1 — cross-module import

public class GetEntraAccessUsersHandler : IRequestHandler<...>
{
    private readonly IGraphService _graphService;  // line 9 — UserManagement-internal service

    public GetEntraAccessUsersHandler(IGraphService graphService) => _graphService = graphService;

    public async Task<...> Handle(..., CancellationToken ct)
    {
        var users = await _graphService.GetAppRoleMembersAsync(AccessRoles.Base, ct);  // line 15
```

`IGraphService` is a UserManagement-internal service interface (`UserManagement/Services/IGraphService.cs`). The Authorization module consuming it directly violates module isolation.

The correct pattern is already established in this codebase for the identical scenario: the `IArticleUserResolver` contract lives in `Article/Contracts/` (consumer-owned), `GraphArticleUserResolver` in `UserManagement/Infrastructure/` implements it (provider adapter), and `UserManagementModule` registers the binding. Authorization should follow the same shape.

## Why it matters
`development_guidelines.md` forbids direct cross-module service access: "Communication between modules **exclusively through `contracts/`**." By injecting `IGraphService` directly, `Authorization` is coupled to UserManagement's internal abstraction. Any rename, split, or evolution of `IGraphService` (adding a method, changing its return type) becomes a breaking change for the Authorization module, and the isolation guarantee that lets each module evolve independently is broken.

## Suggested fix
Minimal two-step fix:

1. Add a consumer-owned contract in `Authorization/Contracts/IEntraAccessUserSource.cs`:
   ```csharp
   public interface IEntraAccessUserSource
   {
       Task<List<UserManagement.Dtos.EntraUserDto>> GetBaseMembersAsync(CancellationToken ct);
   }
   ```

2. Add an adapter in `UserManagement/Infrastructure/EntraAccessUserSourceAdapter.cs` that delegates to `IGraphService`, and register it in `UserManagementModule`. `GetEntraAccessUsersHandler` injects `IEntraAccessUserSource` instead.

This mirrors exactly how `IArticleUserResolver` / `GraphArticleUserResolver` is structured.

---
_Filed by daily arch-review routine on 2026-06-26._
