# Fix Nightly E2E Unauthenticated (issue #3680) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Grant the E2E test synthetic user the `super_user` role so `/api/auth/me` returns full permissions, restoring the sidebar/nav in E2E runs and clearing ~321 nightly failures.

**Architecture:** The frontend nav is gated by the permission list returned from `GET /api/auth/me` (`GetMeHandler`), which returns all permissions only for `super_user`; otherwise it uses the DB permission resolver, which returns empty for the unknown E2E synthetic user. The E2E synthetic user (`E2ESessionService.CreateSyntheticUserClaims`) currently has `Base` + piecemeal per-module role claims but not `super_user`, so `/api/auth/me` returns empty permissions and the sidebar collapses to Dashboard. Adding a single `super_user` role claim routes the E2E user through the existing wildcard path (identical to how `MockAuthenticationHandler` treats mock users), populating the full permission list.

**Tech Stack:** .NET 8, xUnit + FluentAssertions + Moq, MediatR. Backend-only change; no frontend or workflow changes.

---

## Root Cause (why this is the fix)

- Failing run [#29553600888](https://github.com/onpaj/Anela.Heblo/actions/runs/29553600888) log shows backend E2E auth **succeeds**: `✅ E2E authentication successful on attempt 1` (cookie issued). Live staging confirms `env-info` → `Staging` and `POST /api/e2etest/auth` (no token) → `400` (endpoints enabled).
- Reproduced live: browser to `https://heblo.stg.anela.cz/?e2e=true` → `isE2ETestMode()`=true, `useE2EAuth` authenticates client-side, yet `GET /api/auth/me` → 401/empty and sidebar shows only **Dashboard**.
- `GetMeHandler` (`backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetMe/GetMeHandler.cs`): returns `AccessMatrix.AllRoleValues().Append(Base)` only when `IsInRole(SuperUser)`; else calls `IPermissionResolver.ResolveAsync(...)` which "Returns empty for inactive/unknown users."
- `E2ESessionService.CreateSyntheticUserClaims` has no `AccessRoles.SuperUser` claim → empty permissions → nav gone. `PermissionClaimsTransformation` (lines 58-66) grants `super_user` the wildcard regardless of DB. `MockAuthenticationHandler.cs:38-42` already does exactly this for mock users.
- Workflow env/secrets are unchanged vs `b286a66^`; that commit only made result-counting honest, unmasking this pre-existing break.

## File Structure

- Modify: `backend/src/Anela.Heblo.API/Infrastructure/Authentication/E2ESessionService.cs` — add one `super_user` role claim in `CreateSyntheticUserClaims`. One responsibility: build the E2E synthetic identity.
- Modify (test): `backend/test/Anela.Heblo.Tests/Authorization/E2ESessionServiceTests.cs` — add a regression `[Fact]` asserting the `super_user` claim is present.
- Reference only (no change): `GetMeHandler.cs` (already returns all permissions for super_user, proven by `GetMeHandlerTests.Handle_SuperUser_ReturnsAllPermissionsAndIsSuperUser`), `PermissionClaimsTransformation.cs`, `MockAuthenticationHandler.cs`, `frontend/src/auth/PermissionsContext.tsx`, `frontend/src/api/hooks/usePermissions.ts`.

## Non-goals

- Do **not** delete the existing per-module role claims (they become redundant under `super_user`, but removal is deferred to keep this surgical).
- `baleni`'s 3 non-auth failures are a separate fixture issue — out of scope.
- No frontend, workflow, or `appsettings` changes.

---

### Task 1: Grant the E2E synthetic user the `super_user` role

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Infrastructure/Authentication/E2ESessionService.cs` (inside `CreateSyntheticUserClaims`, right after the `AccessRoles.Base` claim)
- Test: `backend/test/Anela.Heblo.Tests/Authorization/E2ESessionServiceTests.cs`

- [ ] **Step 1: Write the failing regression test**

Add this `[Fact]` to `backend/test/Anela.Heblo.Tests/Authorization/E2ESessionServiceTests.cs` (after the existing `CreateSyntheticUserClaims_StillIncludesExistingRoles_RegressionGuard` test, before the closing brace):

```csharp
    [Fact]
    public void CreateSyntheticUserClaims_IncludesSuperUserRole()
    {
        // E2E test user must be super_user so GET /api/auth/me returns the full
        // permission wildcard and the frontend sidebar/nav renders (issue #3680).
        var sut = CreateSut();

        var claims = sut.CreateSyntheticUserClaims("Staging");

        claims.Should().Contain(c =>
            c.Type == ClaimTypes.Role && c.Value == AccessRoles.SuperUser);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~E2ESessionServiceTests.CreateSyntheticUserClaims_IncludesSuperUserRole"
```
Expected: FAIL — assertion "Expected claims to contain ... super_user" (the claim is not yet present).

- [ ] **Step 3: Add the `super_user` claim (minimal implementation)**

In `backend/src/Anela.Heblo.API/Infrastructure/Authentication/E2ESessionService.cs`, locate the existing line inside `CreateSyntheticUserClaims`:

```csharp
            new Claim(ClaimTypes.Role, AccessRoles.Base), // Base role for application access
```

Insert immediately **after** it:

```csharp
            // E2E test user is a super_user: full access via the same wildcard path as mock
            // auth (MockAuthenticationHandler) and production break-glass. This is what
            // populates the frontend permission list from /api/auth/me (GetMeHandler returns
            // the wildcard for super_user), which the sidebar/RequireMenuPath gate on. Without
            // it the nav collapses to Dashboard and every role-gated E2E page times out (#3680).
            // The per-module role claims below are now redundant under super_user but kept as
            // harmless defense-in-depth.
            new Claim(ClaimTypes.Role, AccessRoles.SuperUser),
```

- [ ] **Step 4: Run the new test to verify it passes**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~E2ESessionServiceTests.CreateSyntheticUserClaims_IncludesSuperUserRole"
```
Expected: PASS (1 passed).

- [ ] **Step 5: Run the full authorization test suite to confirm no regressions**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Authorization"
```
Expected: PASS — including the existing `E2ESessionServiceTests.*`, `GetMeHandlerTests.Handle_SuperUser_ReturnsAllPermissionsAndIsSuperUser` (which already proves super_user → full permissions), and `GetMeHandlerTests.Handle_RegularUser_*`. The existing E2E claim tests use `.Should().Contain(...)` (presence checks) and remain green.

- [ ] **Step 6: Build and format**

Run:
```bash
cd backend && dotnet build && dotnet format --verify-no-changes
```
Expected: Build succeeded, 0 warnings/errors; format reports no changes (run `dotnet format` without the flag first if it reports differences).

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.API/Infrastructure/Authentication/E2ESessionService.cs \
        backend/test/Anela.Heblo.Tests/Authorization/E2ESessionServiceTests.cs
git commit -m "fix: grant E2E synthetic user super_user so /api/auth/me returns full permissions (#3680)"
```

---

## Post-merge verification (requires staging deploy)

The E2E cookie flow needs the deployed staging image, so full end-to-end confirmation happens after deploy:

1. Trigger `e2e-nightly-regression.yml` via `workflow_dispatch` for a single module (e.g. `core` or `stock-operations`).
2. Confirm the run's page snapshots show the full sidebar (Katalog, Sklad, Marketing, Administrace) and contain **no** "Click to authenticate".
3. Manual smoke (once deployed): establish the E2E session (`POST /api/e2etest/auth` with a valid SP bearer token, set `sessionStorage['e2e-test-token']`), load `?e2e=true`, and confirm `GET /api/auth/me` returns `isSuperUser: true` with a non-empty `permissions` array and the sidebar renders all modules.
4. **Definition of done (#3680):** nightly (or single-module) re-run shows the full sidebar, no "Click to authenticate" in snapshots, and the ~321 auth-symptom failures clear.
