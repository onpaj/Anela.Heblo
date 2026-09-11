### task: add-authorization-directory-adapter

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Authorization/Infrastructure/AuthorizationUserDirectorySourceAdapter.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Authorization/AuthorizationModule.cs`

**Depends on:** add-user-directory-contract

- [ ] **Step 1: Write the adapter**

```csharp
using Anela.Heblo.Application.Shared.Users.Contracts;
using Anela.Heblo.Domain.Features.Authorization;

namespace Anela.Heblo.Application.Features.Authorization.Infrastructure;

internal sealed class AuthorizationUserDirectorySourceAdapter : IUserDirectorySource
{
    private readonly IAuthorizationRepository _repository;

    public AuthorizationUserDirectorySourceAdapter(IAuthorizationRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<UserDirectoryEntry>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var users = await _repository.GetAllUsersAsync(cancellationToken);

        return users
            .Select(u => new UserDirectoryEntry
            {
                EntraObjectId = u.EntraObjectId,
                Email = u.Email,
                DisplayName = u.DisplayName,
            })
            .ToList();
    }
}
```

- [ ] **Step 2: Register the DI binding in `AuthorizationModule.cs`**

Open `backend/src/Anela.Heblo.Application/Features/Authorization/AuthorizationModule.cs`. Add the using and one registration line, keeping every existing line unchanged:

```csharp
using Anela.Heblo.Application.Features.Authorization.Infrastructure;
using Anela.Heblo.Application.Shared.Users.Contracts;
```

Inside `AddAuthorizationModule`, immediately after the existing `services.AddScoped<IAuthorizationRepository, AuthorizationRepository>();` line, add:

```csharp
        services.AddScoped<IUserDirectorySource, AuthorizationUserDirectorySourceAdapter>();
```

- [ ] **Step 3: Build**

Run: `cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Authorization/Infrastructure/AuthorizationUserDirectorySourceAdapter.cs backend/src/Anela.Heblo.Application/Features/Authorization/AuthorizationModule.cs
git commit -m "feat(authorization): implement IUserDirectorySource via adapter"
```

---
