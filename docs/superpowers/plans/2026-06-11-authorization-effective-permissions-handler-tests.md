# Authorization — GetUserEffectivePermissionsHandler Unit Tests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add four xUnit `[Fact]` tests for `GetUserEffectivePermissionsHandler` to characterize and lock in its three branches (user-not-found, inactive-user, active-user) plus the dedup edge for `AccessRoles.Base`. These are characterization tests on existing production behavior — they MUST pass green on first run against the unchanged handler. Their value is regression protection going forward, especially the security-critical `IsActive` short-circuit.

**Architecture:** One new test file under `backend/test/Anela.Heblo.Tests/Authorization/`. No production code changes. Pure handler-in-isolation tests using `Mock<IAuthorizationRepository>` (default loose behavior, `Verify(..., Times.Never)` for negative-interaction assertions), inline data construction, FluentAssertions. Mirrors `GetMeHandlerTests.cs` style.

**Tech Stack:** xUnit, FluentAssertions, Moq — all already referenced by `Anela.Heblo.Tests`.

---

## File Structure

**New file:**
- `backend/test/Anela.Heblo.Tests/Authorization/GetUserEffectivePermissionsHandlerTests.cs` — test class containing four `[Fact]` methods, one per branch.

**Existing files referenced (no edits):**
- `backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetUserEffectivePermissions/GetUserEffectivePermissionsHandler.cs` — system under test.
- `backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetUserEffectivePermissions/GetUserEffectivePermissionsRequest.cs` — request DTO; `UserId` property.
- `backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetUserEffectivePermissions/GetUserEffectivePermissionsResponse.cs` — response DTO; `Permissions` list, inherits `BaseResponse` (`Success`, `ErrorCode`).
- `backend/src/Anela.Heblo.Domain/Features/Authorization/IAuthorizationRepository.cs` — repository interface; relevant methods `GetUserByIdAsync(Guid, CancellationToken)` and `GetGroupGraphAsync(CancellationToken)` returning `(List<GroupPermission>, List<GroupParent>)`.
- `backend/src/Anela.Heblo.Domain/Features/Authorization/Entities/AppUser.cs` — has settable properties `Id`, `Email`, `DisplayName`, `IsActive`, `UserGroups`.
- `backend/src/Anela.Heblo.Domain/Features/Authorization/Entities/UserGroup.cs` — `UserId`, `GroupId`.
- `backend/src/Anela.Heblo.Domain/Features/Authorization/Entities/GroupPermission.cs` — `GroupId`, `PermissionValue`.
- `backend/src/Anela.Heblo.Domain/Features/Authorization/Entities/GroupParent.cs` — `GroupId`, `ParentGroupId`.
- `backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs` — `AccessRoles.Base = "heblo_user"`.
- `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` — `AuthorizationUserNotFound = 3202`.
- `backend/src/Anela.Heblo.Application/Shared/BaseResponse.cs` — `bool Success`, `ErrorCodes? ErrorCode`.

**Reference for style (sibling tests):**
- `backend/test/Anela.Heblo.Tests/Authorization/GetMeHandlerTests.cs` — closest stylistic match.

---

## Task 1: Scaffold test class with user-not-found fact (FR-2)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Authorization/GetUserEffectivePermissionsHandlerTests.cs`

- [ ] **Step 1: Create the file with the namespace, usings, class, and the first `[Fact]`**

Write the file with these exact contents:

```csharp
using Anela.Heblo.Application.Features.Authorization.UseCases.GetUserEffectivePermissions;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Authorization.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class GetUserEffectivePermissionsHandlerTests
{
    [Fact]
    public async Task Handle_UserNotFound_ReturnsAuthorizationUserNotFoundAndDoesNotLoadGraph()
    {
        var userId = Guid.NewGuid();
        var repo = new Mock<IAuthorizationRepository>();
        repo.Setup(r => r.GetUserByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AppUser?)null);

        var handler = new GetUserEffectivePermissionsHandler(repo.Object);
        var response = await handler.Handle(
            new GetUserEffectivePermissionsRequest { UserId = userId },
            CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.AuthorizationUserNotFound);
        repo.Verify(r => r.GetGroupGraphAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 2: Build the test project**

Run from the worktree root:

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: Build succeeds (0 errors).

If the build fails on missing usings, recheck Step 1 against the file paths listed under "File Structure". Do not add project references — `Anela.Heblo.Tests` already references `Application`, `Domain`, and `Persistence`.

- [ ] **Step 3: Run the new fact and verify it PASSES**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetUserEffectivePermissionsHandlerTests.Handle_UserNotFound_ReturnsAuthorizationUserNotFoundAndDoesNotLoadGraph" \
  --no-build -v minimal
```

Expected: 1 passed, 0 failed.

Rationale for "expected PASS" rather than "expected FAIL": these are characterization tests on already-shipped production behavior. The handler at `GetUserEffectivePermissionsHandler.cs:18-19` already returns `new GetUserEffectivePermissionsResponse(ErrorCodes.AuthorizationUserNotFound)` for a null user. A failing test here would mean either (a) the assertion is wrong, or (b) the implementation was silently changed — either way, stop and investigate before continuing.

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Authorization/GetUserEffectivePermissionsHandlerTests.cs
git commit -m "test(authorization): cover GetUserEffectivePermissionsHandler user-not-found path"
```

---

## Task 2: Add inactive-user fact (FR-3, security-critical)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Authorization/GetUserEffectivePermissionsHandlerTests.cs`

This fact is the security regression test. It must assert (a) empty permissions, (b) `AccessRoles.Base` absent, and (c) `GetGroupGraphAsync` never called. All three together are the guard. If any future refactor moves the graph load above the `IsActive` check, this test fails — that is the point.

- [ ] **Step 1: Add the second `[Fact]` method below the first one inside the class**

Insert the following method immediately before the closing brace of the `GetUserEffectivePermissionsHandlerTests` class (i.e., after the first fact's closing brace):

```csharp
    [Fact]
    public async Task Handle_InactiveUser_ReturnsEmptyPermissionsAndDoesNotLoadGraph()
    {
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var user = new AppUser
        {
            Id = userId,
            Email = "u@x.cz",
            DisplayName = "U",
            IsActive = false,
            UserGroups = new List<UserGroup>
            {
                new() { UserId = userId, GroupId = groupId }
            }
        };
        var repo = new Mock<IAuthorizationRepository>();
        repo.Setup(r => r.GetUserByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var handler = new GetUserEffectivePermissionsHandler(repo.Object);
        var response = await handler.Handle(
            new GetUserEffectivePermissionsRequest { UserId = userId },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        response.ErrorCode.Should().BeNull();
        response.Permissions.Should().BeEmpty();
        response.Permissions.Should().NotContain(AccessRoles.Base);
        repo.Verify(r => r.GetGroupGraphAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
```

- [ ] **Step 2: Build**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: Build succeeds.

- [ ] **Step 3: Run the new fact and verify it PASSES**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetUserEffectivePermissionsHandlerTests.Handle_InactiveUser_ReturnsEmptyPermissionsAndDoesNotLoadGraph" \
  --no-build -v minimal
```

Expected: 1 passed, 0 failed.

If this fact fails, the `!user.IsActive` guard at `GetUserEffectivePermissionsHandler.cs:21-22` may have regressed — STOP and report before continuing.

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Authorization/GetUserEffectivePermissionsHandlerTests.cs
git commit -m "test(authorization): cover inactive-user short-circuit (security regression)"
```

---

## Task 3: Add active-user merge/sort fact (FR-4)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Authorization/GetUserEffectivePermissionsHandlerTests.cs`

This fact exercises the full active path: `GetGroupGraphAsync` is called, the closure (`GroupClosure.Resolve`) pulls in parent-group permissions, `AccessRoles.Base` is unioned in, the result is deduplicated by `HashSet<string>`, and the final list is ordered via `OrderBy(p => p)`.

Group setup chosen so that the closure produces 3 permissions ("permA", "permB", "permC") via one parent edge:
- `UserGroups` → `G1` directly.
- `GroupPermission`: `(G1, "permB")`, `(G1, "permA")`, `(G2, "permC")` — note the deliberately out-of-order seed to prove the `OrderBy` is doing real work.
- `GroupParent`: `(G1 → G2)` — so closure visits G1 then G2, picking up `permC` transitively.

Expected final order (default `OrderBy(p => p)` string comparison, culture-aware but identical to ordinal for ASCII inputs): `["heblo_user", "permA", "permB", "permC"]`.

- [ ] **Step 1: Add the third `[Fact]` method**

Insert immediately after the second fact:

```csharp
    [Fact]
    public async Task Handle_ActiveUser_ReturnsMergedDistinctSortedPermissionsIncludingBase()
    {
        var userId = Guid.NewGuid();
        var g1 = Guid.NewGuid();
        var g2 = Guid.NewGuid();
        var user = new AppUser
        {
            Id = userId,
            Email = "u@x.cz",
            DisplayName = "U",
            IsActive = true,
            UserGroups = new List<UserGroup>
            {
                new() { UserId = userId, GroupId = g1 }
            }
        };
        var perms = new List<GroupPermission>
        {
            new() { GroupId = g1, PermissionValue = "permB" },
            new() { GroupId = g1, PermissionValue = "permA" },
            new() { GroupId = g2, PermissionValue = "permC" }
        };
        var parents = new List<GroupParent>
        {
            new() { GroupId = g1, ParentGroupId = g2 }
        };

        var repo = new Mock<IAuthorizationRepository>();
        repo.Setup(r => r.GetUserByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        repo.Setup(r => r.GetGroupGraphAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((perms, parents));

        var handler = new GetUserEffectivePermissionsHandler(repo.Object);
        var response = await handler.Handle(
            new GetUserEffectivePermissionsRequest { UserId = userId },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        response.ErrorCode.Should().BeNull();
        response.Permissions.Should().Equal(AccessRoles.Base, "permA", "permB", "permC");
    }
```

- [ ] **Step 2: Build**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: Build succeeds.

- [ ] **Step 3: Run the new fact and verify it PASSES**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetUserEffectivePermissionsHandlerTests.Handle_ActiveUser_ReturnsMergedDistinctSortedPermissionsIncludingBase" \
  --no-build -v minimal
```

Expected: 1 passed, 0 failed.

If this fact fails on ordering, double-check the expected sequence: `AccessRoles.Base` is `"heblo_user"` which sorts before `"permA"`/`"permB"`/`"permC"` in both ordinal and culture-aware comparison ('h' < 'p').

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Authorization/GetUserEffectivePermissionsHandlerTests.cs
git commit -m "test(authorization): cover active-user permission merge/dedup/sort"
```

---

## Task 4: Add dedup-of-Base fact (arch-review amendment)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Authorization/GetUserEffectivePermissionsHandlerTests.cs`

The arch review recommended promoting the dedup-edge note from "if needed" to a dedicated fourth `[Fact]`. This pins down `HashSet` dedup behavior when a group explicitly grants `AccessRoles.Base` — it must still appear exactly once in the output.

- [ ] **Step 1: Add the fourth `[Fact]` method**

Insert immediately after the third fact:

```csharp
    [Fact]
    public async Task Handle_ActiveUser_WhenGroupAlsoGrantsBase_BaseAppearsOnlyOnce()
    {
        var userId = Guid.NewGuid();
        var g1 = Guid.NewGuid();
        var user = new AppUser
        {
            Id = userId,
            Email = "u@x.cz",
            DisplayName = "U",
            IsActive = true,
            UserGroups = new List<UserGroup>
            {
                new() { UserId = userId, GroupId = g1 }
            }
        };
        var perms = new List<GroupPermission>
        {
            new() { GroupId = g1, PermissionValue = AccessRoles.Base },
            new() { GroupId = g1, PermissionValue = "permX" }
        };
        var parents = new List<GroupParent>();

        var repo = new Mock<IAuthorizationRepository>();
        repo.Setup(r => r.GetUserByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        repo.Setup(r => r.GetGroupGraphAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((perms, parents));

        var handler = new GetUserEffectivePermissionsHandler(repo.Object);
        var response = await handler.Handle(
            new GetUserEffectivePermissionsRequest { UserId = userId },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Permissions.Should().Equal(AccessRoles.Base, "permX");
        response.Permissions.Count(p => p == AccessRoles.Base).Should().Be(1);
    }
```

- [ ] **Step 2: Build**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: Build succeeds.

- [ ] **Step 3: Run the new fact and verify it PASSES**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetUserEffectivePermissionsHandlerTests.Handle_ActiveUser_WhenGroupAlsoGrantsBase_BaseAppearsOnlyOnce" \
  --no-build -v minimal
```

Expected: 1 passed, 0 failed.

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Authorization/GetUserEffectivePermissionsHandlerTests.cs
git commit -m "test(authorization): pin down AccessRoles.Base dedup when group also grants it"
```

---

## Task 5: Run the full class + project validation

**Files:**
- No edits. Validation only.

- [ ] **Step 1: Run all four facts together**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetUserEffectivePermissionsHandlerTests" \
  --no-build -v minimal
```

Expected: 4 passed, 0 failed. Total elapsed under 1 second.

- [ ] **Step 2: Run `dotnet format` on the new file**

```bash
dotnet format backend/Anela.Heblo.sln --include backend/test/Anela.Heblo.Tests/Authorization/GetUserEffectivePermissionsHandlerTests.cs
```

Expected: Exits 0 with no changes (or applies whitespace normalization). If formatting changed the file, stage and amend the last commit.

- [ ] **Step 3: Confirm no production code under `backend/src/` was modified**

```bash
git diff --name-only main...HEAD -- backend/src/
```

Expected: empty output. If any `backend/src/` file appears, this plan was violated — STOP and report.

- [ ] **Step 4: Run the full Authorization test folder to confirm no regressions in sibling tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Authorization" \
  --no-build -v minimal
```

Expected: All Authorization-namespace tests pass (existing count + 4 new = total).

No further commits in this task — validation only.

---

## Self-Review Notes

**Spec coverage check:**
- FR-1 (test class wiring): Task 1 creates the file at the exact path, in the exact namespace, with `[Fact]` methods following the `MethodUnderTest_State_ExpectedResult` style. ✓
- FR-2 (user-not-found): Task 1 fact asserts `Success==false`, `ErrorCode==AuthorizationUserNotFound`, and `GetGroupGraphAsync` never called. ✓
- FR-3 (inactive-user, security-critical): Task 2 fact asserts empty permissions, `AccessRoles.Base` not contained, and `GetGroupGraphAsync` never called — all three guards present per spec. ✓
- FR-4 (active-user merge/sort): Task 3 fact uses `Should().Equal(...)` (order-preserving) against `[AccessRoles.Base, "permA", "permB", "permC"]` with a parent-graph edge to prove closure resolution. ✓
- FR-4 dedup edge (arch-review promotion): Task 4 is a dedicated fourth fact. ✓
- FR-5 (no shared fixtures): All construction is inline; no new files outside the single test file; no production-code changes (validated by Task 5 Step 3). ✓
- NFR-1 (performance < 200ms/fact, < 1s total): Task 5 Step 1 expects total elapsed < 1s. Each fact is in-memory, mocked. ✓
- NFR-2 (security): FR-3 assertions cover all three required dimensions. Placeholder identifiers (`"u@x.cz"`, no real tokens) used throughout. ✓
- NFR-3 (maintainability): xUnit + FluentAssertions + Moq loose, method names describe behavior, no needless comments. ✓

**Placeholder scan:** No "TBD"/"add appropriate"/"similar to" — every step has the literal code or command.

**Type consistency:** `AppUser` properties (`Id`, `Email`, `DisplayName`, `IsActive`, `UserGroups`), `UserGroup` (`UserId`, `GroupId`), `GroupPermission` (`GroupId`, `PermissionValue`), `GroupParent` (`GroupId`, `ParentGroupId`), `GetUserEffectivePermissionsRequest.UserId`, `GetUserEffectivePermissionsResponse.{Success, ErrorCode, Permissions}`, `IAuthorizationRepository.{GetUserByIdAsync(Guid, CancellationToken), GetGroupGraphAsync(CancellationToken)}` — all verified against source files listed in "File Structure" and used consistently across Tasks 1–4.

**Ordering caveat (per arch-review):** `OrderBy(p => p)` on strings uses the default culture-aware comparer, not ordinal. For the test inputs (`heblo_user`, `permA`, `permB`, `permC`, `permX`) the two orderings agree. No test change is needed; no comment is added (per project no-comments rule — the test name and inputs are self-explanatory).
