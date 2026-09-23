### task: relocate-exception-files

**Files:**
- Create (via move): `backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/Exceptions/GraphServiceAuthException.cs`
- Create (via move): `backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/Exceptions/GraphServiceException.cs`
- Delete (via move): `backend/src/Anela.Heblo.Application/Features/UserManagement/Contracts/GraphServiceAuthException.cs`
- Delete (via move): `backend/src/Anela.Heblo.Application/Features/UserManagement/Contracts/GraphServiceException.cs`

- [ ] **Step 1: Create the new folder and move both files with `git mv` (preserves history)**

Run:
```bash
cd backend/src/Anela.Heblo.Application/Features/UserManagement
mkdir -p Infrastructure/Exceptions
git mv Contracts/GraphServiceAuthException.cs Infrastructure/Exceptions/GraphServiceAuthException.cs
git mv Contracts/GraphServiceException.cs Infrastructure/Exceptions/GraphServiceException.cs
```
Expected: both files now show under `Infrastructure/Exceptions/` in `git status` as renames, nothing left under `Contracts/`.

- [ ] **Step 2: Update the namespace in the moved `GraphServiceAuthException.cs`**

Replace the file's full contents with:
```csharp
namespace Anela.Heblo.Application.Features.UserManagement.Infrastructure.Exceptions;

/// <summary>
/// Thrown by <see cref="Anela.Heblo.Application.Features.UserManagement.Services.IGraphService"/> implementations when token acquisition
/// or authentication for the underlying identity provider fails.
/// Wraps infrastructure-specific auth exceptions (e.g. MsalException) so that
/// Application-layer consumers remain decoupled from SDK packages.
/// </summary>
public sealed class GraphServiceAuthException : Exception
{
    public GraphServiceAuthException(string message, Exception innerException)
        : base(message, innerException) { }
}
```
Note: the `<see cref="IGraphService"/>` doc comment is fully qualified here since `IGraphService` now lives in a sibling namespace (`Services`) rather than the exception's own namespace — this avoids an unresolved cref warning. This is the only content change beyond the namespace line; the class body, constructor, and summary text are otherwise unchanged.

- [ ] **Step 3: Update the namespace in the moved `GraphServiceException.cs`**

Replace the file's full contents with:
```csharp
namespace Anela.Heblo.Application.Features.UserManagement.Infrastructure.Exceptions;

/// <summary>
/// Thrown by <see cref="Anela.Heblo.Application.Features.UserManagement.Services.IGraphService"/> implementations when the remote
/// directory service returns an error response (e.g. an OData error from Microsoft Graph).
/// Wraps infrastructure-specific service exceptions so that Application-layer consumers
/// remain decoupled from SDK packages.
/// </summary>
public sealed class GraphServiceException : Exception
{
    public GraphServiceException(string message, Exception innerException)
        : base(message, innerException) { }
}
```

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/UserManagement/Contracts/GraphServiceAuthException.cs \
        backend/src/Anela.Heblo.Application/Features/UserManagement/Contracts/GraphServiceException.cs \
        backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/Exceptions/GraphServiceAuthException.cs \
        backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/Exceptions/GraphServiceException.cs
git commit -m "refactor(usermanagement): move Graph exceptions from Contracts to Infrastructure/Exceptions"
```
Expected: commit succeeds. The build will fail at this point (consumers not yet updated) — that is expected and fixed in the next task.

---
