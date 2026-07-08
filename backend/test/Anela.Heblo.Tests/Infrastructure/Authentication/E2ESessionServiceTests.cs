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
