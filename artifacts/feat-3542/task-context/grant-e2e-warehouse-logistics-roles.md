### task: grant-e2e-warehouse-logistics-roles

**Goal (FR-1, per spec + architect's Specification Amendment 1):** Grant the E2E synthetic user BOTH `AccessRoles.WarehouseLogisticsRead` and `AccessRoles.WarehouseLogisticsWrite` — unconditionally, not just Read. This is not a judgment call: `box-creation.spec.ts` performs `POST /api/transport-boxes` and `box-receive.spec.ts` performs `POST .../open-by-code` / `PUT .../state`, all of which are guarded by `[FeatureAuthorize(Feature.Warehouse_Logistics, AccessLevel.Write)]` on `TransportBoxController` (confirmed at `backend/src/Anela.Heblo.API/Controllers/TransportBoxController.cs` lines 81, 96, 116, 137, 161, 201 — every write action requires Write; only the class-level Read gate at line 19 covers GET endpoints).

**File to modify:** `backend/src/Anela.Heblo.API/Infrastructure/Authentication/E2ESessionService.cs`

Current `CreateSyntheticUserClaims` method body (lines 70–92):

```csharp
    public Claim[] CreateSyntheticUserClaims(string environmentName)
    {
        _logger.LogDebug("E2E Session: Creating synthetic user claims for environment: {Environment}", environmentName);

        return new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "e2e-test-user-id"),
            new Claim(ClaimTypes.Name, "E2E Test User"),
            new Claim(ClaimTypes.Email, "e2e-test@anela-heblo.com"),
            new Claim("preferred_username", "e2e-test@anela-heblo.com"),
            new Claim("name", "E2E Test User"),
            new Claim("given_name", "E2E"),
            new Claim("family_name", "Test"),
            new Claim("oid", "e2e-test-object-id"),
            new Claim("tid", environmentName), // Use environment as tenant for testing
            new Claim(ClaimTypes.Role, AccessRoles.Base), // Base role for application access
            new Claim("scp", "access_as_user"),
            // Grant the finance overview read role so E2E tests can reach /api/FinancialOverview.
            // FeatureAuthorize checks the role claim (permission strings were renamed away from the
            // old "FinancialOverview.View" form), so a stale "permission" claim no longer matches.
            new Claim(ClaimTypes.Role, AccessRoles.FinanceFinancialOverviewRead)
        };
    }
```

Use the Edit tool with this exact `old_string`/`new_string` pair (only the last two lines of the array change):

old_string:
```csharp
            // Grant the finance overview read role so E2E tests can reach /api/FinancialOverview.
            // FeatureAuthorize checks the role claim (permission strings were renamed away from the
            // old "FinancialOverview.View" form), so a stale "permission" claim no longer matches.
            new Claim(ClaimTypes.Role, AccessRoles.FinanceFinancialOverviewRead)
        };
```

new_string:
```csharp
            // Grant the finance overview read role so E2E tests can reach /api/FinancialOverview.
            // FeatureAuthorize checks the role claim (permission strings were renamed away from the
            // old "FinancialOverview.View" form), so a stale "permission" claim no longer matches.
            new Claim(ClaimTypes.Role, AccessRoles.FinanceFinancialOverviewRead),
            // Grant Warehouse_Logistics Read+Write so E2E tests can reach the Transport Box pages
            // and API (TransportBoxController requires Read at the class level and Write on every
            // create/receive/state-change action). Read alone unlocks navigation
            // (RequireMenuPath/Sidebar gate on Feature.Warehouse_Logistics) but every write-triggering
            // E2E interaction — box-creation.spec.ts (POST /api/transport-boxes) and
            // box-receive.spec.ts (POST .../open-by-code, PUT .../state) — needs Write too.
            new Claim(ClaimTypes.Role, AccessRoles.WarehouseLogisticsRead),
            new Claim(ClaimTypes.Role, AccessRoles.WarehouseLogisticsWrite)
        };
```

**Do not touch:** `access-matrix.json`, `AccessRoles.generated.cs`, `AccessMatrix.generated.cs`, or `frontend/src/auth/accessMatrix.generated.ts`. These are auto-generated (the first three lines of `AccessRoles.generated.cs` say "AUTO-GENERATED... Do not edit by hand") and already define `WarehouseLogisticsRead = "warehouse.logistics.read"` and `WarehouseLogisticsWrite = "warehouse.logistics.write"` (confirmed at `backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs` lines 37–38). `E2ESessionService` only *consumes* these constants — it is hand-written application code, not generated.

**Test-first: write the failing test before making the change above.**

Create a new file `backend/test/Anela.Heblo.Tests/Authorization/E2ESessionServiceTests.cs` (this file does not currently exist — confirmed by directory listing of `backend/test/Anela.Heblo.Tests/Authorization/`). This test project already has a `ProjectReference` to `Anela.Heblo.API.csproj` (confirmed in `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`), so `E2ESessionService` is directly constructible. Follow the existing pattern for instantiating a service with a `NullLogger<T>` (see `backend/test/Anela.Heblo.Tests/Marketing/OutlookCalendarSyncServiceTests.cs` line 55) and the pattern for referencing API-layer types directly in this test namespace (see `backend/test/Anela.Heblo.Tests/Authorization/DashboardControllerAuthorizationTests.cs`, which does `using Anela.Heblo.API.Controllers;`).

Write the full file content exactly as follows:

```csharp
using System.Security.Claims;
using Anela.Heblo.API.Infrastructure.Authentication;
using Anela.Heblo.Domain.Features.Authorization;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class E2ESessionServiceTests
{
    private static E2ESessionService CreateSut() =>
        new E2ESessionService(NullLogger<E2ESessionService>.Instance);

    [Fact]
    public void CreateSyntheticUserClaims_IncludesWarehouseLogisticsReadAndWrite()
    {
        var sut = CreateSut();

        var claims = sut.CreateSyntheticUserClaims("Staging");

        claims.Should().Contain(c =>
            c.Type == ClaimTypes.Role && c.Value == AccessRoles.WarehouseLogisticsRead);
        claims.Should().Contain(c =>
            c.Type == ClaimTypes.Role && c.Value == AccessRoles.WarehouseLogisticsWrite);
    }

    [Fact]
    public void CreateSyntheticUserClaims_StillIncludesExistingRoles_RegressionGuard()
    {
        var sut = CreateSut();

        var claims = sut.CreateSyntheticUserClaims("Staging");

        claims.Should().Contain(c =>
            c.Type == ClaimTypes.Role && c.Value == AccessRoles.Base);
        claims.Should().Contain(c =>
            c.Type == ClaimTypes.Role && c.Value == AccessRoles.FinanceFinancialOverviewRead);
    }

    [Fact]
    public void CreateSyntheticUserClaims_IncludesIdentityClaims()
    {
        var sut = CreateSut();

        var claims = sut.CreateSyntheticUserClaims("Staging");

        claims.Should().Contain(c =>
            c.Type == ClaimTypes.NameIdentifier && c.Value == "e2e-test-user-id");
        claims.Should().Contain(c =>
            c.Type == ClaimTypes.Email && c.Value == "e2e-test@anela-heblo.com");
    }
}
```

**Run the test before the fix (must fail on the first assertion):**

```bash
cd /home/user/worktrees/feature-3542-E2e-Transport-Box-Pages-Fail-To-Render-Create-Rece/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~E2ESessionServiceTests"
```

Expected output before the fix: `CreateSyntheticUserClaims_IncludesWarehouseLogisticsReadAndWrite` FAILS (the claim array does not yet contain `WarehouseLogisticsRead`/`WarehouseLogisticsWrite`); the other two tests pass.

**Now apply the `E2ESessionService.cs` edit above**, then re-run the same command. Expected output after the fix: all 3 tests pass, e.g. `Passed! - Failed: 0, Passed: 3, Skipped: 0, Total: 3`.

**Full validation:**

```bash
cd /home/user/worktrees/feature-3542-E2e-Transport-Box-Pages-Fail-To-Render-Create-Rece/backend
dotnet build Anela.Heblo.sln
dotnet format Anela.Heblo.sln --verify-no-changes
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Authorization"
```

All Authorization-namespace tests (including the pre-existing `FeatureAuthorizeAttributeTests`, `AccessMatrixTests`, `AccessMatrixJsonTests`, etc.) must continue to pass — this change does not touch any generated file or shared authorization infrastructure, only the E2E-only claim list.

**Commit:**

```bash
cd /home/user/worktrees/feature-3542-E2e-Transport-Box-Pages-Fail-To-Render-Create-Rece
git add backend/src/Anela.Heblo.API/Infrastructure/Authentication/E2ESessionService.cs backend/test/Anela.Heblo.Tests/Authorization/E2ESessionServiceTests.cs
git commit -m "$(cat <<'EOF'
Grant E2E test user Warehouse_Logistics Read+Write roles

The E2E synthetic user only held Base and FinanceFinancialOverviewRead,
so every Transport Box page/API call was silently redirected (frontend
RequireMenuPath gate) or 403'd (TransportBoxController's
FeatureAuthorize(Warehouse_Logistics)). Both Read and Write are granted
because box-creation.spec.ts and box-receive.spec.ts perform real write
actions (create/open-by-code/state-change) that require the Write role.

Fixes 12 of 18 nightly transport E2E failures reported in run #191.
EOF
)"
```

---

