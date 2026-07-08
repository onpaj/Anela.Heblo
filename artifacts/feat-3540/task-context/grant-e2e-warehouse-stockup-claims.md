### task: grant-e2e-warehouse-stockup-claims

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Infrastructure/Authentication/E2ESessionService.cs:85-91`
- Test: `backend/test/Anela.Heblo.Tests/Infrastructure/Authentication/E2ESessionServiceTests.cs` (new file)

**Goal:** Grant the E2E synthetic test principal `warehouse.stock_up.read`/`write` role claims so
`[FeatureAuthorize(Feature.Warehouse_StockUp)]` stops rejecting its calls to
`/api/StockUpOperations*` with 403.

- [ ] Step 1: Create the test file `backend/test/Anela.Heblo.Tests/Infrastructure/Authentication/E2ESessionServiceTests.cs` with this exact content:

  ```csharp
  using System.Linq;
  using System.Security.Claims;
  using Anela.Heblo.API.Infrastructure.Authentication;
  using Anela.Heblo.Domain.Features.Authorization;
  using FluentAssertions;
  using Microsoft.Extensions.Logging;
  using Microsoft.Extensions.Logging.Abstractions;
  using Xunit;

  namespace Anela.Heblo.Tests.Infrastructure.Authentication;

  /// <summary>
  /// Regression coverage for the E2E synthetic user's role claims. A new
  /// [FeatureAuthorize]-gated feature shipping without a matching role claim added to
  /// CreateSyntheticUserClaims() has already caused two incidents: FinancialOverview
  /// (fixed previously) and Warehouse_StockUp (feat-3540, 56 nightly E2E failures).
  /// </summary>
  public class E2ESessionServiceTests
  {
      private readonly ILogger<E2ESessionService> _logger = NullLogger<E2ESessionService>.Instance;

      [Fact]
      public void CreateSyntheticUserClaims_IncludesWarehouseStockUpReadAndWriteRoles()
      {
          // Arrange
          var sut = new E2ESessionService(_logger);

          // Act
          var claims = sut.CreateSyntheticUserClaims("Staging");

          // Assert
          var roleClaims = claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value);
          roleClaims.Should().Contain(AccessRoles.WarehouseStockUpRead,
              "the E2E principal must be able to call GET /api/StockUpOperations, which is " +
              "gated by [FeatureAuthorize(Feature.Warehouse_StockUp)] (Read)");
          roleClaims.Should().Contain(AccessRoles.WarehouseStockUpWrite,
              "the E2E principal must be able to call POST /api/StockUpOperations/{id}/retry " +
              "and /accept, which are gated at AccessLevel.Write");
      }
  }
  ```

- [ ] Step 2: Run the new test and confirm it fails (red) against the current, unfixed
  `E2ESessionService`:
  ```
  cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~E2ESessionServiceTests
  ```
  Expect `CreateSyntheticUserClaims_IncludesWarehouseStockUpReadAndWriteRoles` to fail because
  neither role claim is present yet.

- [ ] Step 3: In `backend/src/Anela.Heblo.API/Infrastructure/Authentication/E2ESessionService.cs`,
  replace:
  ```csharp
              new Claim(ClaimTypes.Role, AccessRoles.Base), // Base role for application access
              new Claim("scp", "access_as_user"),
              // Grant the finance overview read role so E2E tests can reach /api/FinancialOverview.
              // FeatureAuthorize checks the role claim (permission strings were renamed away from the
              // old "FinancialOverview.View" form), so a stale "permission" claim no longer matches.
              new Claim(ClaimTypes.Role, AccessRoles.FinanceFinancialOverviewRead)
          };
  ```
  with:
  ```csharp
              new Claim(ClaimTypes.Role, AccessRoles.Base), // Base role for application access
              new Claim("scp", "access_as_user"),
              // Grant the finance overview read role so E2E tests can reach /api/FinancialOverview.
              // FeatureAuthorize checks the role claim (permission strings were renamed away from the
              // old "FinancialOverview.View" form), so a stale "permission" claim no longer matches.
              new Claim(ClaimTypes.Role, AccessRoles.FinanceFinancialOverviewRead),
              // Grant the Warehouse_StockUp read/write roles so E2E tests can reach
              // /api/StockUpOperations* (list, retry, accept). Without these, FeatureAuthorize
              // rejects every request with 403 before the controller action runs (feat-3540).
              new Claim(ClaimTypes.Role, AccessRoles.WarehouseStockUpRead),
              new Claim(ClaimTypes.Role, AccessRoles.WarehouseStockUpWrite)
          };
  ```

- [ ] Step 4: Re-run the same filtered test command from Step 2 and confirm it now passes (green).

- [ ] Step 5: Run the full backend test suite to check for regressions, then build:
  ```
  cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj && dotnet build && dotnet format --verify-no-changes
  ```
  If `dotnet format` reports changes, run `dotnet format` (no `--verify-no-changes`) and re-check
  the diff only touches the two files above.

- [ ] Step 6: Commit with message `fix(e2e): grant Warehouse_StockUp read/write claims to E2E synthetic user (feat-3540)`.

---
