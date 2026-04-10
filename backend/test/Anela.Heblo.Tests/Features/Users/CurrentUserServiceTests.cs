using System.Security.Claims;
using Anela.Heblo.Application.Features.Users;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Users;

public class CurrentUserServiceTests
{
    private static CurrentUserService CreateService(ClaimsPrincipal principal)
    {
        var httpContext = new DefaultHttpContext { User = principal };
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);
        return new CurrentUserService(accessor.Object);
    }

    private static ClaimsPrincipal Authenticated(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    [Fact]
    public void GetCurrentUser_WhenSubClaim_ReturnsSubAsId()
    {
        var principal = Authenticated(new Claim("sub", "sub-123"));
        var service = CreateService(principal);

        var user = service.GetCurrentUser();

        Assert.Equal("sub-123", user.Id);
    }

    [Fact]
    public void GetCurrentUser_WhenNameIdentifierClaim_ReturnsItAsId()
    {
        var principal = Authenticated(new Claim(ClaimTypes.NameIdentifier, "ni-456"));
        var service = CreateService(principal);

        var user = service.GetCurrentUser();

        Assert.Equal("ni-456", user.Id);
    }

    [Fact]
    public void GetCurrentUser_WhenOnlyOidClaim_ReturnsOidAsId()
    {
        // Entra ID tokens always carry 'oid' (Object ID). Some token configurations
        // (guest B2B users, certain OAuth flows) omit 'sub' and 'nameidentifier'.
        // Without this fallback the handlers throw InvalidOperationException → HTTP 500.
        var principal = Authenticated(new Claim("oid", "oid-789"));
        var service = CreateService(principal);

        var user = service.GetCurrentUser();

        Assert.Equal("oid-789", user.Id);
    }

    [Fact]
    public void GetCurrentUser_WhenEmailClaim_ReturnsEmail()
    {
        var principal = Authenticated(new Claim(ClaimTypes.Email, "user@test.com"));
        var service = CreateService(principal);

        var user = service.GetCurrentUser();

        Assert.Equal("user@test.com", user.Email);
    }

    [Fact]
    public void GetCurrentUser_WhenUnauthenticated_ReturnsAnonymous()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());
        var service = CreateService(principal);

        var user = service.GetCurrentUser();

        Assert.False(user.IsAuthenticated);
        Assert.Equal("Anonymous", user.Name);
    }
}
