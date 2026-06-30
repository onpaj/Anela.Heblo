### task: create-usermanagement-adapter

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/EntraAccessUserSourceAdapter.cs`

- [ ] **Step 1: Create the adapter.**

  The adapter is `internal sealed` — it must NOT be `public`. It lives in the `Infrastructure/`
  folder alongside `GraphArticleUserResolver`. It injects `IGraphService` (UserManagement's own
  interface) and maps the result to `EntraAccessUserRecord` (Authorization's contract type).
  No exception wrapping — see architecture decision in spec.

  ```csharp
  using Anela.Heblo.Application.Features.Authorization.Contracts;
  using Anela.Heblo.Application.Features.UserManagement.Services;
  using Anela.Heblo.Domain.Features.Authorization;

  namespace Anela.Heblo.Application.Features.UserManagement.Infrastructure;

  internal sealed class EntraAccessUserSourceAdapter : IEntraAccessUserSource
  {
      private readonly IGraphService _graph;

      public EntraAccessUserSourceAdapter(IGraphService graph) => _graph = graph;

      public async Task<List<EntraAccessUserRecord>> GetBaseMembersAsync(CancellationToken ct)
      {
          var users = await _graph.GetAppRoleMembersAsync(AccessRoles.Base, ct);
          return users
              .Select(u => new EntraAccessUserRecord(u.Id, u.Email, u.DisplayName))
              .ToList();
      }
  }
  ```

  Note on `AccessRoles.Base`: `AccessRoles` is a generated static class at
  `backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs` in the
  `Anela.Heblo.Domain.Features.Authorization` namespace. The value `Base` was previously used
  directly in the handler — copy it here verbatim.

- [ ] **Step 2: Verify the file compiles.**

  ```bash
  dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
  ```

  Expected: build succeeds.

---
