using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Anela.Heblo.Domain.Features.Authorization;

namespace Anela.Heblo.API.Infrastructure.Authentication;

/// <summary>
/// Service for managing E2E test authentication sessions
/// Handles synthetic user session creation and authentication cookie management
/// </summary>
public interface IE2ESessionService
{
    /// <summary>
    /// Creates an E2E authentication session with synthetic user claims
    /// </summary>
    /// <param name="httpContext">The HTTP context to sign in the user</param>
    /// <param name="environmentName">The environment name to use as tenant</param>
    /// <returns>Task representing the async operation</returns>
    Task CreateE2EAuthenticationSessionAsync(HttpContext httpContext, string environmentName);

    /// <summary>
    /// Creates synthetic user claims for E2E testing
    /// </summary>
    /// <param name="environmentName">The environment name to use as tenant</param>
    /// <returns>Array of claims representing the synthetic user</returns>
    Claim[] CreateSyntheticUserClaims(string environmentName);
}

/// <summary>
/// Implementation of E2E session management service
/// Provides synthetic user session creation for E2E testing scenarios
/// </summary>
public class E2ESessionService : IE2ESessionService
{
    private readonly ILogger<E2ESessionService> _logger;

    public E2ESessionService(ILogger<E2ESessionService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Creates an E2E authentication session with synthetic user claims
    /// Signs in the synthetic user using cookie authentication scheme
    /// </summary>
    public async Task CreateE2EAuthenticationSessionAsync(HttpContext httpContext, string environmentName)
    {
        _logger.LogInformation("E2E Session: Creating authenticated session for synthetic user");

        // Create synthetic user claims
        var claims = CreateSyntheticUserClaims(environmentName);
        var identity = new ClaimsIdentity(claims, "E2ETest");
        var principal = new ClaimsPrincipal(identity);

        // Sign in the synthetic user using the cookie authentication scheme
        // This ensures compatibility with the E2E test session management
        await httpContext.SignInAsync("E2ETestCookies", principal, new AuthenticationProperties
        {
            IsPersistent = false,
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1)
        });

        _logger.LogInformation("E2E Session: Created authenticated session for synthetic user");
    }

    /// <summary>
    /// Creates comprehensive synthetic user claims for E2E testing
    /// Includes all necessary claims for application functionality
    /// </summary>
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
            // E2E test user is a super_user: full access via the same wildcard path as mock
            // auth (MockAuthenticationHandler) and production break-glass. This is what
            // populates the frontend permission list from /api/auth/me (GetMeHandler returns
            // the wildcard for super_user), which the sidebar/RequireMenuPath gate on. Without
            // it the nav collapses to Dashboard and every role-gated E2E page times out (#3680).
            // The per-module role claims below are now redundant under super_user but kept as
            // harmless defense-in-depth.
            new Claim(ClaimTypes.Role, AccessRoles.SuperUser),
            new Claim("scp", "access_as_user"),
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
            new Claim(ClaimTypes.Role, AccessRoles.WarehouseLogisticsWrite),
            // Grant the Warehouse_StockUp read/write roles so E2E tests can reach
            // /api/StockUpOperations* (list, retry, accept). Without these, FeatureAuthorize
            // rejects every request with 403 before the controller action runs (feat-3540).
            new Claim(ClaimTypes.Role, AccessRoles.WarehouseStockUpRead),
            new Claim(ClaimTypes.Role, AccessRoles.WarehouseStockUpWrite),
            // Grant Manufacture_MaterialContainers read/write so the terminal lot-identification
            // E2E test can seed Unassigned containers (POST /api/material-containers/print-labels)
            // and assign them (POST /api/material-containers). Both actions require Write.
            new Claim(ClaimTypes.Role, AccessRoles.ManufactureMaterialContainersRead),
            new Claim(ClaimTypes.Role, AccessRoles.ManufactureMaterialContainersWrite),
            // Grant Purchase_PurchaseOrders read/write so the PO receive flow can list in-transit
            // orders (PoPickStep), read a PO's lines (PoLinePickStep), and update PO status
            // (FinishPoStep — PUT status). List/detail need Read; status update needs Write.
            new Claim(ClaimTypes.Role, AccessRoles.PurchasePurchaseOrdersRead),
            new Claim(ClaimTypes.Role, AccessRoles.PurchasePurchaseOrdersWrite)
        };
    }
}