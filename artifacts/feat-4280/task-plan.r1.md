# Relocate GraphService Exceptions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move `GraphServiceAuthException` and `GraphServiceException` from `UserManagement/Contracts/` to `UserManagement/Infrastructure/Exceptions/`, matching the documented `filesystem.md` template, and fix every reference (8 files) plus 2 stale doc comments, with zero behavior change.

**Architecture:** Pure move + namespace rename of two sealed exception classes. `git mv` preserves history; namespace changes to match the new folder; every consuming file's `using` directives are corrected on a per-file basis depending on whether that file also references `UserDto` from the (unchanged) `Contracts` namespace. No interfaces, signatures, or catch/throw semantics change.

**Tech Stack:** .NET 8 / C#, xUnit, Moq, FluentAssertions.

---

## Important: file inventory (verified by full-repo grep, not just the GitHub issue)

The GitHub issue named only 4 referencing files. A repo-wide grep for `GraphServiceAuthException`/`GraphServiceException` (see `arch-review.r1.md` for the full methodology) found 4 more the issue missed. **Use this list, not the issue's list:**

1. `backend/src/Anela.Heblo.Application/Features/UserManagement/Contracts/GraphServiceAuthException.cs` (move)
2. `backend/src/Anela.Heblo.Application/Features/UserManagement/Contracts/GraphServiceException.cs` (move)
3. `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IGraphService.cs` (using: keep Contracts, add Infrastructure.Exceptions)
4. `backend/src/Anela.Heblo.Application/Features/UserManagement/UseCases/GetGroupMembers/GetGroupMembersHandler.cs` (using: keep Contracts, add Infrastructure.Exceptions)
5. `backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/EntraAccessUserSourceAdapter.cs` (using: keep Contracts, add Infrastructure.Exceptions)
6. `backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/GraphArticleUserResolver.cs` (using: **replace** Contracts with Infrastructure.Exceptions)
7. `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs` (using: keep Contracts, add Infrastructure.Exceptions) — **not in the original issue**
8. `backend/test/Anela.Heblo.Tests/Features/UserManagement/GetGroupMembersHandlerTests.cs` (using: keep Contracts, add Infrastructure.Exceptions) — **not in the original issue**
9. `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs` (using: keep Contracts, add Infrastructure.Exceptions) — **not in the original issue**
10. `backend/test/Anela.Heblo.Tests/Features/UserManagement/EntraAccessUserSourceAdapterTests.cs` (using: keep Contracts, add Infrastructure.Exceptions) — **not in the original issue**
11. `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` (2 text-only comment/message fixes at lines 908 and 977, no using change) — **not in the original issue**

---

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

### task: update-test-usings-and-comments

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/UserManagement/GetGroupMembersHandlerTests.cs:1-7`
- Modify: `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs:1-14`
- Modify: `backend/test/Anela.Heblo.Tests/Features/UserManagement/EntraAccessUserSourceAdapterTests.cs:1-7`
- Modify: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs:908`, `:977`

- [ ] **Step 1: `GetGroupMembersHandlerTests.cs` — add the new using, keep `Contracts` (needed for `UserDto`)**

Current top of file:
```csharp
using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using Anela.Heblo.Application.Features.UserManagement.UseCases.GetGroupMembers;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Shared;
```
Change to:
```csharp
using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using Anela.Heblo.Application.Features.UserManagement.UseCases.GetGroupMembers;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Infrastructure.Exceptions;
using Anela.Heblo.Application.Shared;
```

- [ ] **Step 2: `GraphServiceTests.cs` — add the new using, keep `Contracts`**

Current top of file:
```csharp
using System.Net;
using Anela.Heblo.Adapters.Microsoft365;
using Anela.Heblo.Adapters.Microsoft365.UserManagement;
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;
using Moq;
```
Change to:
```csharp
using System.Net;
using Anela.Heblo.Adapters.Microsoft365;
using Anela.Heblo.Adapters.Microsoft365.UserManagement;
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Infrastructure.Exceptions;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;
using Moq;
```

- [ ] **Step 3: `EntraAccessUserSourceAdapterTests.cs` — add the new using, keep `Contracts`**

Current top of file:
```csharp
using Anela.Heblo.Application.Features.Authorization.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Infrastructure;
using Anela.Heblo.Application.Features.UserManagement.Services;
using FluentAssertions;
using Moq;
using Xunit;
```
Change to:
```csharp
using Anela.Heblo.Application.Features.Authorization.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Infrastructure;
using Anela.Heblo.Application.Features.UserManagement.Infrastructure.Exceptions;
using Anela.Heblo.Application.Features.UserManagement.Services;
using FluentAssertions;
using Moq;
using Xunit;
```

- [ ] **Step 4: `ModuleBoundariesTests.cs` — fix the 2 stale comment/message occurrences (text only, no logic change)**

At line 908 (inside the `SdkExceptionAllowlist` header comment), change:
```csharp
    // GraphServiceException) defined in UserManagement.Contracts instead.
```
to:
```csharp
    // GraphServiceException) defined in UserManagement.Infrastructure.Exceptions instead.
```

At line 977 (inside the `violations.Should().BeEmpty(...)` message string), change:
```csharp
            "UserManagement.Contracts) instead, or add an allowlist entry with justification " +
```
to:
```csharp
            "UserManagement.Infrastructure.Exceptions) instead, or add an allowlist entry with justification " +
```
Do not touch any other line in this file — in particular, do not touch the `SdkExceptionAllowlist` entries themselves (`MeetingTasks.Services.GraphPlannerService`, `CatalogDocuments.Services.GraphCatalogDocumentsStorage`) or the reflection/enumeration logic; they are unrelated to this change.

- [ ] **Step 5: Build and run the full test suite**

Run: `cd backend && dotnet build`
Expected: 0 errors across all projects (production + test).

Run: `dotnet test --filter "FullyQualifiedName~UserManagement|FullyQualifiedName~ModuleBoundariesTests"`
Expected: all `UserManagement`-namespace tests pass (including `GetGroupMembersHandlerTests`, `GraphServiceTests`, `EntraAccessUserSourceAdapterTests`), and `ModuleBoundariesTests.Application_types_should_not_catch_SDK_exception_types_directly` still passes (its enforcement logic is unaffected — see design.r1.md).

- [ ] **Step 6: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/UserManagement/GetGroupMembersHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs \
        backend/test/Anela.Heblo.Tests/Features/UserManagement/EntraAccessUserSourceAdapterTests.cs \
        backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
git commit -m "test(usermanagement): fix usings and stale comments for relocated Graph exceptions"
```

---

### task: final-verification

**Files:** none (verification only, no code changes expected)

- [ ] **Step 1: Confirm no remaining references to the old namespace**

Run:
```bash
grep -rn "UserManagement.Contracts.GraphServiceAuthException\|UserManagement.Contracts.GraphServiceException" backend/ || echo "clean"
```
Expected: `clean` (no fully-qualified references to the old namespace remain). This also implicitly checks that no file still relies on the old `Contracts` location for these two types.

- [ ] **Step 2: Confirm the full set of 8 referencing files still resolves correctly**

Run:
```bash
grep -rl "GraphServiceAuthException\|GraphServiceException" backend/ | sort
```
Expected output: exactly these 9 paths (the 2 moved files themselves plus the 7 consumers, i.e. 8 consumers from the inventory above, since `ModuleBoundariesTests.cs` matches on text, not a `using`, but is expected to still appear here since the class names remain in its comment/message text):
```
backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs
backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/EntraAccessUserSourceAdapter.cs
backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/Exceptions/GraphServiceAuthException.cs
backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/Exceptions/GraphServiceException.cs
backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/GraphArticleUserResolver.cs
backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IGraphService.cs
backend/src/Anela.Heblo.Application/Features/UserManagement/UseCases/GetGroupMembers/GetGroupMembersHandler.cs
backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
backend/test/Anela.Heblo.Tests/Features/UserManagement/EntraAccessUserSourceAdapterTests.cs
backend/test/Anela.Heblo.Tests/Features/UserManagement/GetGroupMembersHandlerTests.cs
backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs
```
If any other path appears, investigate before proceeding — it means a reference was missed by this plan's inventory.

- [ ] **Step 3: Full build and format check**

Run: `cd backend && dotnet build`
Expected: 0 errors, 0 new warnings (in particular, no unused-`using` warnings — this catches any file where a `Contracts` using should have been removed but wasn't, e.g. `GraphArticleUserResolver.cs`).

Run: `dotnet format --verify-no-changes`
Expected: no formatting diffs. If it reports diffs, run `dotnet format` (no `--verify-no-changes`) and re-commit.

- [ ] **Step 4: Full test suite**

Run: `cd backend && dotnet test`
Expected: all tests pass, no failures or skips introduced by this change.

- [ ] **Step 5: Final commit (only if `dotnet format` made changes in Step 3)**

```bash
git add -A
git commit -m "style(usermanagement): dotnet format after Graph exception relocation"
```
If `dotnet format --verify-no-changes` passed cleanly in Step 3, skip this step — there is nothing to commit.
