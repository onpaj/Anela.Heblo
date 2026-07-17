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
}
