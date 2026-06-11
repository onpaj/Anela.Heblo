# Fix RBAC user pairing on first login Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop the new RBAC system from creating a duplicate `AppUser` row on a user's first login. After this fix, the pre-created row from `EntraMemberSearch` is correctly matched at first login and gets its permissions, instead of being orphaned next to a permission-less duplicate.

**Architecture:** The bug lives in one file: `PermissionClaimsTransformation` reads `ClaimTypes.NameIdentifier` from the JWT as the "Entra object ID". With `Microsoft.AspNetCore.Authentication.JwtBearer`'s default inbound claim mapping, that's the `sub` claim (per-user-per-app pairwise) — not the tenant-wide `oid` claim that Graph (and therefore the pre-created `AppUser.EntraObjectId`) uses. Fix: read `oid` via Microsoft.Identity.Web's `ClaimsPrincipal.GetObjectId()` helper, which checks both the raw `oid` claim and the mapped `objectidentifier` URI. Add tests that pin the realistic Entra token shape so this can't silently regress.

**Tech Stack:** .NET 8, ASP.NET Core, Microsoft.Identity.Web 2.x, Microsoft.AspNetCore.Authentication.JwtBearer 8.0.8, xUnit, FluentAssertions, Moq.

---

## File Structure

- `backend/src/Anela.Heblo.API/Infrastructure/Authentication/PermissionClaimsTransformation.cs` — modified. Replace the 3‑line claim fallback with `GetObjectId()` + `NameIdentifier` fallback. Add `using Microsoft.Identity.Web;`.
- `backend/src/Anela.Heblo.API/Infrastructure/Authentication/MockAuthenticationHandler.cs` — modified. Change `ClaimTypes.NameIdentifier` value at line 25 to match the existing `oid` value at line 33, so anywhere that still reads `NameIdentifier` sees the same ID as `GetObjectId()`.
- `backend/test/Anela.Heblo.Tests/Authorization/PermissionClaimsTransformationTests.cs` — modified. Add three new tests (realistic Entra `oid` claim, mapped `objectidentifier` URI, mock-only `NameIdentifier` fallback). Refit two existing tests to use the realistic claim shape.

No new files. No frontend changes. No EF migrations (orphan rows from past reproduction get cleaned up by a one-off SQL snippet in the deployment appendix).

---

### Task 1: Pin the bug with a failing test

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Authorization/PermissionClaimsTransformationTests.cs`

- [ ] **Step 1: Add the failing test**

Open `backend/test/Anela.Heblo.Tests/Authorization/PermissionClaimsTransformationTests.cs` and append this test inside the existing `PermissionClaimsTransformationTests` class (after the four existing `[Fact]` methods, before the closing brace):

```csharp
    [Fact]
    public async Task Transform_EntraToken_UsesOidClaim_NotNameIdentifier()
    {
        // Real Entra JWT: contains both "oid" (tenant-wide object ID, used by Graph)
        // and a NameIdentifier mapped from "sub" (per-user-per-app pairwise pseudonymous ID).
        // The resolver MUST be called with the oid, not the sub — otherwise the pre-created
        // AppUser row (keyed by oid from EntraMemberSearch) is missed and a duplicate is JIT-created.
        const string realOid = "11111111-2222-3333-4444-555555555555";
        const string subValue = "sub-value-different-from-oid";

        var resolver = new Mock<IPermissionResolver>(MockBehavior.Strict);
        resolver
            .Setup(r => r.ResolveAsync(realOid, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EffectivePermissions(false, new[] { "catalog.read" }, new[] { "G" }));

        var sut = new PermissionClaimsTransformation(resolver.Object);
        var principal = Principal(
            new Claim("oid", realOid),
            new Claim(ClaimTypes.NameIdentifier, subValue));

        var result = await sut.TransformAsync(principal);

        result.IsInRole("catalog.read").Should().BeTrue();
        resolver.Verify(
            r => r.ResolveAsync(realOid, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        resolver.VerifyNoOtherCalls();
    }
```

- [ ] **Step 2: Run the test to confirm it fails**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~PermissionClaimsTransformationTests.Transform_EntraToken_UsesOidClaim_NotNameIdentifier"
```

Expected: FAIL with a Moq strict-mode exception — the resolver is being called with `subValue` (read from `NameIdentifier`) instead of `realOid`. This confirms the bug: the current code reads `NameIdentifier` before `oid`.

- [ ] **Step 3: Commit the failing test**

```bash
git add backend/test/Anela.Heblo.Tests/Authorization/PermissionClaimsTransformationTests.cs
git commit -m "test(authz): pin oid-vs-sub mismatch in PermissionClaimsTransformation"
```

---

### Task 2: Make the test pass by reading `oid`

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Infrastructure/Authentication/PermissionClaimsTransformation.cs`

- [ ] **Step 1: Add the `Microsoft.Identity.Web` using**

Open `backend/src/Anela.Heblo.API/Infrastructure/Authentication/PermissionClaimsTransformation.cs`. Add `using Microsoft.Identity.Web;` to the using block so the top of the file reads:

```csharp
using System.Security.Claims;
using Anela.Heblo.Domain.Features.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Identity.Web;
```

`Microsoft.Identity.Web` is already a transitive package of the API project (used in `AuthenticationExtensions.cs`), so no `.csproj` change is needed.

- [ ] **Step 2: Replace the claim lookup**

Find lines 25-27 (the current `var objectId = …` block) and replace them with the mapping-agnostic helper. The current code:

```csharp
        var objectId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? principal.FindFirst("oid")?.Value
                       ?? principal.FindFirst("sub")?.Value;
```

becomes:

```csharp
        // GetObjectId() reads both "oid" (raw) and "http://schemas.microsoft.com/identity/claims/objectidentifier"
        // (the URI the JwtBearer handler renames "oid" to when MapInboundClaims is on).
        // This is the tenant-wide Entra Object ID — same value Graph returns as /users/{id}.id,
        // which is what EntraMemberSearch stores in AppUser.EntraObjectId.
        // NameIdentifier fallback is for mock auth (and any other scheme without an oid claim).
        var objectId = principal.GetObjectId()
                       ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
```

- [ ] **Step 3: Run the failing test — it should now pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~PermissionClaimsTransformationTests.Transform_EntraToken_UsesOidClaim_NotNameIdentifier"
```

Expected: PASS.

- [ ] **Step 4: Run the full PermissionClaimsTransformationTests class — make sure nothing else broke yet**

```bash
dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~PermissionClaimsTransformationTests"
```

Expected: the new test PASSES. The two existing tests that build a principal with `new Claim(ClaimTypes.NameIdentifier, "oid-1")` (`Transform_RegularUser_AddsResolvedPermissionsAsRoles` and `Transform_Idempotent_DoesNotDuplicateClaims`) still PASS, because `GetObjectId()` returns null when there's no `oid` claim and falls back to `NameIdentifier`. `Transform_SuperUserRoleInToken_AddsAllPermissions_NoResolverCall` and `Transform_Unauthenticated_ReturnsUnchanged` also pass — neither hits the claim lookup that changed.

- [ ] **Step 5: Commit the fix**

```bash
git add backend/src/Anela.Heblo.API/Infrastructure/Authentication/PermissionClaimsTransformation.cs
git commit -m "fix(authz): read Entra oid via GetObjectId() so first login matches pre-created AppUser"
```

---

### Task 3: Cover the mapped `objectidentifier` URI shape

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Authorization/PermissionClaimsTransformationTests.cs`

- [ ] **Step 1: Add the test**

Append this test below the one added in Task 1, still inside the `PermissionClaimsTransformationTests` class:

```csharp
    [Fact]
    public async Task Transform_EntraToken_UsesObjectIdentifierUri_WhenMapInboundClaimsApplied()
    {
        // When MapInboundClaims is enabled in JwtBearerOptions, the JWT "oid" claim is rewritten
        // to the URI "http://schemas.microsoft.com/identity/claims/objectidentifier".
        // GetObjectId() reads either form; pin that behavior here.
        const string realOid = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";

        var resolver = new Mock<IPermissionResolver>(MockBehavior.Strict);
        resolver
            .Setup(r => r.ResolveAsync(realOid, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EffectivePermissions(false, new[] { "catalog.read" }, new[] { "G" }));

        var sut = new PermissionClaimsTransformation(resolver.Object);
        var principal = Principal(
            new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", realOid),
            new Claim(ClaimTypes.NameIdentifier, "sub-mapped-to-name-identifier"));

        var result = await sut.TransformAsync(principal);

        result.IsInRole("catalog.read").Should().BeTrue();
        resolver.Verify(
            r => r.ResolveAsync(realOid, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        resolver.VerifyNoOtherCalls();
    }
```

- [ ] **Step 2: Run the test — it should pass on the fix from Task 2**

```bash
dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~PermissionClaimsTransformationTests.Transform_EntraToken_UsesObjectIdentifierUri_WhenMapInboundClaimsApplied"
```

Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Authorization/PermissionClaimsTransformationTests.cs
git commit -m "test(authz): cover mapped objectidentifier URI claim shape"
```

---

### Task 4: Cover the mock-auth fallback (NameIdentifier without `oid`)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Authorization/PermissionClaimsTransformationTests.cs`

- [ ] **Step 1: Add the test**

Append below Task 3's test:

```csharp
    [Fact]
    public async Task Transform_NoOidClaim_FallsBackToNameIdentifier()
    {
        // Mock auth scheme (used in dev/test/E2E) doesn't emit "oid" — only NameIdentifier.
        // The fallback must still hand a non-null identifier to the resolver.
        const string mockIdentifier = "mock-only-identifier";

        var resolver = new Mock<IPermissionResolver>(MockBehavior.Strict);
        resolver
            .Setup(r => r.ResolveAsync(mockIdentifier, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EffectivePermissions(false, new[] { "catalog.read" }, Array.Empty<string>()));

        var sut = new PermissionClaimsTransformation(resolver.Object);
        var principal = Principal(new Claim(ClaimTypes.NameIdentifier, mockIdentifier));

        var result = await sut.TransformAsync(principal);

        result.IsInRole("catalog.read").Should().BeTrue();
        resolver.Verify(
            r => r.ResolveAsync(mockIdentifier, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        resolver.VerifyNoOtherCalls();
    }
```

- [ ] **Step 2: Run the test**

```bash
dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~PermissionClaimsTransformationTests.Transform_NoOidClaim_FallsBackToNameIdentifier"
```

Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Authorization/PermissionClaimsTransformationTests.cs
git commit -m "test(authz): cover mock-auth NameIdentifier fallback"
```

---

### Task 5: Refit the two existing tests to a realistic claim shape

The two existing tests (`Transform_RegularUser_AddsResolvedPermissionsAsRoles`, `Transform_Idempotent_DoesNotDuplicateClaims`) currently pass an "oid-1" value through `NameIdentifier` — that's not the shape real Entra tokens have, so they offered no protection against the bug we just fixed. Move them to the realistic shape. Task 4 already covers the legacy mock shape, so coverage isn't lost.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Authorization/PermissionClaimsTransformationTests.cs`

- [ ] **Step 1: Replace the principal in `Transform_RegularUser_AddsResolvedPermissionsAsRoles`**

Find this line in the existing test:

```csharp
        var principal = Principal(new Claim(ClaimTypes.NameIdentifier, "oid-1"));
```

Replace it with:

```csharp
        var principal = Principal(new Claim("oid", "oid-1"));
```

- [ ] **Step 2: Replace the principal in `Transform_Idempotent_DoesNotDuplicateClaims`**

Find this line in the existing test:

```csharp
        var principal = Principal(new Claim(ClaimTypes.NameIdentifier, "oid-1"));
```

Replace it with:

```csharp
        var principal = Principal(new Claim("oid", "oid-1"));
```

- [ ] **Step 3: Run the full test class — everything passes**

```bash
dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~PermissionClaimsTransformationTests"
```

Expected: all 7 tests pass (4 original + 3 new).

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Authorization/PermissionClaimsTransformationTests.cs
git commit -m "test(authz): refit existing tests to use realistic oid claim shape"
```

---

### Task 6: Align `MockAuthenticationHandler` NameIdentifier with its `oid`

`GetObjectId()` will now return `"00000000-0000-0000-0000-000000000000"` for mock auth (the value at `MockAuthenticationHandler.cs:33`), while `NameIdentifier` still emits `"mock-user-id"`. Anywhere else in the codebase that still reads `NameIdentifier` would see a different identifier than the resolver does. A quick grep (`mock-user-id`) shows only that one line in the whole backend uses the literal, so changing it is safe and keeps the mock principal internally consistent.

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Infrastructure/Authentication/MockAuthenticationHandler.cs`

- [ ] **Step 1: Update the NameIdentifier value**

Open `backend/src/Anela.Heblo.API/Infrastructure/Authentication/MockAuthenticationHandler.cs`. Line 25 currently reads:

```csharp
            new Claim(ClaimTypes.NameIdentifier, "mock-user-id"),
```

Replace it with:

```csharp
            new Claim(ClaimTypes.NameIdentifier, "00000000-0000-0000-0000-000000000000"),
```

This is the same GUID already emitted as the `"oid"` claim on line 33.

- [ ] **Step 2: Run the full backend test suite**

```bash
dotnet test backend/test/Anela.Heblo.Tests
```

Expected: every test passes. If any test fails on the literal `"mock-user-id"`, the grep missed something — update that test to the GUID and re-run.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.API/Infrastructure/Authentication/MockAuthenticationHandler.cs
git commit -m "refactor(authz): align mock auth NameIdentifier with its oid claim"
```

---

### Task 7: Final validation gates

- [ ] **Step 1: Build**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: build succeeds with no new warnings.

- [ ] **Step 2: Format**

```bash
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```

Expected: exit 0 (no formatting drift). If it fails, run without `--verify-no-changes`, then `git add` + amend the last commit or add a follow-up `chore: dotnet format` commit.

- [ ] **Step 3: Full backend test suite**

```bash
dotnet test backend/test/Anela.Heblo.Tests
```

Expected: all tests pass.

- [ ] **Step 4: Run staging end-to-end check (manual)**

Pick a tenant user with no `AppUser` row in the staging DB.

```sql
-- Confirm no pre-existing row (run against the staging Heblo_TST DB)
SELECT * FROM public."AppUsers" WHERE "Email" = '<their-email>';
```

Through the admin UI, navigate to a group and add that user via the "Add Entra user" search box. Verify a new row was created with the user's Entra Object ID:

```sql
SELECT "Id", "EntraObjectId", "Email", "LastLoginAt"
FROM public."AppUsers" WHERE "Email" = '<their-email>';
```

Cross-check `EntraObjectId` against Azure Portal → Users → that user → Object ID. They should be identical (a single GUID).

Have the user sign in. Then re-run the same SELECT:

- There must still be exactly **one** row for that email (no duplicate).
- `LastLoginAt` is now populated.
- The user's UI shows the permissions of the group they were added to.

---

## Deployment appendix: one-shot cleanup for prior duplicates

You've reproduced the bug at least once, so the staging DB likely contains orphan rows where `EntraObjectId` is a `sub` value. After deploying the code fix, run this SQL against `Heblo_TST` (and, if ever applicable, `Heblo`).

```sql
-- 1. List duplicate-by-email rows; the "good" row has groups, the orphan has none.
SELECT u."Id",
       u."EntraObjectId",
       u."Email",
       u."LastLoginAt",
       (SELECT COUNT(*) FROM public."UserGroups" g WHERE g."UserId" = u."Id") AS group_count
FROM public."AppUsers" u
WHERE u."Email" IN (
    SELECT "Email" FROM public."AppUsers" GROUP BY "Email" HAVING COUNT(*) > 1
)
ORDER BY u."Email", group_count DESC;

-- 2. For each email with two rows: delete the row with group_count = 0.
-- Replace <orphan-id> with the Id from the query above.
-- DELETE FROM public."AppUsers" WHERE "Id" = '<orphan-id>';
```

After deletion, the next time the affected user logs in, the code fix in Task 2 matches them to the surviving row (now keyed by their real `oid`) and updates `LastLoginAt`. No manual `EntraObjectId` patching is required because the surviving row already carries the correct `oid` (it was set by EntraMemberSearch).

---

## What this plan does **not** change

- `PermissionResolver.cs` — its find-or-create logic was already correct given a correct lookup key. We're feeding it the right key now, that's all.
- `AppUser` schema or any EF migration — the `EntraObjectId` unique index is the right constraint; we don't need an `Email` unique constraint to fix this bug, and adding one would conflict with the orphan rows that exist today.
- Frontend / `EntraMemberSearch` / Graph code — all already correct. The bug was purely in claim selection at login.
