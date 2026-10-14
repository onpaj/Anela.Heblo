### task: update-production-usings

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IGraphService.cs:1`
- Modify: `backend/src/Anela.Heblo.Application/Features/UserManagement/UseCases/GetGroupMembers/GetGroupMembersHandler.cs:1-3`
- Modify: `backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/EntraAccessUserSourceAdapter.cs:1-4`
- Modify: `backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/GraphArticleUserResolver.cs:1-3`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs:1-2`

- [ ] **Step 1: `IGraphService.cs` — add the new using, keep `Contracts` (needed for `UserDto`)**

Current top of file:
```csharp
using Anela.Heblo.Application.Features.UserManagement.Contracts;

namespace Anela.Heblo.Application.Features.UserManagement.Services;
```
Change to:
```csharp
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Infrastructure.Exceptions;

namespace Anela.Heblo.Application.Features.UserManagement.Services;
```
Nothing else in this file changes — the `<exception cref="GraphServiceAuthException">` / `<exception cref="GraphServiceException">` doc comments now resolve against the new using.

- [ ] **Step 2: `GetGroupMembersHandler.cs` — add the new using, keep `Contracts`**

Current top of file:
```csharp
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using MediatR;
using Microsoft.Extensions.Logging;
```
Change to:
```csharp
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Infrastructure.Exceptions;
using MediatR;
using Microsoft.Extensions.Logging;
```
Nothing else in this file changes — the two `catch (GraphServiceAuthException ex)` / `catch (GraphServiceException ex)` blocks resolve unchanged.

- [ ] **Step 3: `EntraAccessUserSourceAdapter.cs` — add the new using, keep `Contracts`**

Current top of file:
```csharp
using Anela.Heblo.Application.Features.Authorization.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Domain.Features.Authorization;
```
Change to:
```csharp
using Anela.Heblo.Application.Features.Authorization.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Infrastructure.Exceptions;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Domain.Features.Authorization;
```
Nothing else in this file changes.

- [ ] **Step 4: `GraphArticleUserResolver.cs` — replace `Contracts` with the new using (this file has no other reason to import `Contracts`)**

Current top of file:
```csharp
using Anela.Heblo.Application.Features.Article.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Services;
```
Change to:
```csharp
using Anela.Heblo.Application.Features.Article.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Infrastructure.Exceptions;
using Anela.Heblo.Application.Features.UserManagement.Services;
```
Nothing else in this file changes — verify no other symbol from `UserManagement.Contracts` is referenced in this file before removing the line (it currently is not: the only members it touches are `m.Id`/`m.DisplayName` via `var`, and the two exception types).

- [ ] **Step 5: `GraphService.cs` (adapter, `Anela.Heblo.Adapters.Microsoft365`) — add the new using, keep `Contracts`**

Current top of file:
```csharp
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Web;
using Microsoft.Identity.Client;
using System.Net.Http;
```
Change to:
```csharp
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Infrastructure.Exceptions;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Web;
using Microsoft.Identity.Client;
using System.Net.Http;
```
Nothing else in this file changes — its `throw new GraphServiceException(...)` (2 sites) and `throw new GraphServiceAuthException(...)` (2 sites) and `catch (GraphServiceAuthException)` resolve unchanged.

- [ ] **Step 6: Build to confirm production code compiles**

Run: `cd backend && dotnet build`
Expected: production projects (`Anela.Heblo.Application`, `Anela.Heblo.Adapters.Microsoft365`) build with 0 errors. The test project will still fail to build until the next task fixes its usings — that is expected.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IGraphService.cs \
        backend/src/Anela.Heblo.Application/Features/UserManagement/UseCases/GetGroupMembers/GetGroupMembersHandler.cs \
        backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/EntraAccessUserSourceAdapter.cs \
        backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/GraphArticleUserResolver.cs \
        backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs
git commit -m "refactor(usermanagement): fix production usings for relocated Graph exceptions"
```

---
