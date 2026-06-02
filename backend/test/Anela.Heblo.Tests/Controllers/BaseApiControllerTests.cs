using System.Security.Claims;
using Anela.Heblo.API.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Anela.Heblo.Tests.Controllers;

public class BaseApiControllerTests
{
    private sealed class TestController : BaseApiController
    {
        public string CallGetCurrentUserId() => GetCurrentUserId();
    }

    private static TestController CreateControllerWithClaims(params Claim[] claims)
    {
        var controller = new TestController();
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
        return controller;
    }

    [Fact]
    public void GetCurrentUserId_WhenNameIdentifierPresent_ReturnsNameIdentifier()
    {
        var controller = CreateControllerWithClaims(
            new Claim(ClaimTypes.NameIdentifier, "name-id-123"));

        var result = controller.CallGetCurrentUserId();

        result.Should().Be("name-id-123");
    }

    [Fact]
    public void GetCurrentUserId_WhenOnlySubPresent_ReturnsSub()
    {
        var controller = CreateControllerWithClaims(
            new Claim("sub", "sub-user-456"));

        var result = controller.CallGetCurrentUserId();

        result.Should().Be("sub-user-456");
    }

    [Fact]
    public void GetCurrentUserId_WhenOnlyOidPresent_ReturnsOid()
    {
        var controller = CreateControllerWithClaims(
            new Claim("oid", "oid-user-789"));

        var result = controller.CallGetCurrentUserId();

        result.Should().Be("oid-user-789");
    }

    [Fact]
    public void GetCurrentUserId_WhenMultipleClaimsPresent_PrioritizesNameIdentifier()
    {
        var controller = CreateControllerWithClaims(
            new Claim(ClaimTypes.NameIdentifier, "name-id-123"),
            new Claim("sub", "sub-user-456"),
            new Claim("oid", "oid-user-789"));

        var result = controller.CallGetCurrentUserId();

        result.Should().Be("name-id-123");
    }

    [Fact]
    public void GetCurrentUserId_WhenSubAndOidPresent_PrioritizesSub()
    {
        var controller = CreateControllerWithClaims(
            new Claim("sub", "sub-user-456"),
            new Claim("oid", "oid-user-789"));

        var result = controller.CallGetCurrentUserId();

        result.Should().Be("sub-user-456");
    }

    [Fact]
    public void GetCurrentUserId_WhenNoSupportedClaim_ThrowsUnauthorizedAccessException()
    {
        var controller = CreateControllerWithClaims(); // no claims

        var act = controller.CallGetCurrentUserId;

        act.Should().Throw<UnauthorizedAccessException>()
            .WithMessage("Authenticated user has no identifiable claim.");
    }
}
