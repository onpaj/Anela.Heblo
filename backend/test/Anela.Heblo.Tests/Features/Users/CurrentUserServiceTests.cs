using System.Security.Claims;
using Anela.Heblo.API.Features.Users;
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
    public void GetCurrentUser_WhenOnlyPreferredUsername_ReturnsItAsEmail()
    {
        // Entra ID access tokens omit the `email` claim by default and put the
        // user's email/UPN in `preferred_username`. Without this fallback,
        // production code reading currentUser.Email gets null.
        var principal = Authenticated(new Claim("preferred_username", "ondra@anela.cz"));
        var service = CreateService(principal);

        var user = service.GetCurrentUser();

        Assert.Equal("ondra@anela.cz", user.Email);
    }

    [Fact]
    public void GetCurrentUser_WhenOnlyUpn_ReturnsItAsEmail()
    {
        var principal = Authenticated(new Claim("upn", "ondra@anela.cz"));
        var service = CreateService(principal);

        var user = service.GetCurrentUser();

        Assert.Equal("ondra@anela.cz", user.Email);
    }

    [Fact]
    public void GetCurrentUser_PrefersEmailOverPreferredUsername()
    {
        var principal = Authenticated(
            new Claim(ClaimTypes.Email, "real@anela.cz"),
            new Claim("preferred_username", "upn@anela.cz"));
        var service = CreateService(principal);

        var user = service.GetCurrentUser();

        Assert.Equal("real@anela.cz", user.Email);
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

    [Fact]
    public void GetCurrentUser_PrefersOidOverNameIdentifier_ToMatchClaimsTransformation()
    {
        // Regression: CurrentUserService.Id must match PermissionClaimsTransformation's
        // GetObjectId() lookup key. Otherwise GetMe (which uses this Id to query the
        // permission resolver) finds a different AppUser than [Authorize] checks see,
        // causing 403 responses even when GetMe shows the correct permissions.
        // Entra tokens carry both "oid" (tenant-wide Object ID, used by EntraMemberSearch
        // and PermissionClaimsTransformation) and a NameIdentifier mapped from "sub"
        // (per-user-per-app pairwise pseudonymous ID). The two are different values.
        var principal = Authenticated(
            new Claim(ClaimTypes.NameIdentifier, "sub-pairwise-pseudonym"),
            new Claim("oid", "11111111-2222-3333-4444-555555555555"));
        var service = CreateService(principal);

        var user = service.GetCurrentUser();

        Assert.Equal("11111111-2222-3333-4444-555555555555", user.Id);
    }

    [Fact]
    public void GetCurrentUser_ReadsOidFromObjectIdentifierUri_WhenMapInboundClaimsApplied()
    {
        // When MapInboundClaims is enabled in JwtBearerOptions, the JWT "oid" claim is
        // rewritten to the URI "http://schemas.microsoft.com/identity/claims/objectidentifier".
        // GetObjectId() reads either form; mirror that here.
        var principal = Authenticated(
            new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier",
                "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
        var service = CreateService(principal);

        var user = service.GetCurrentUser();

        Assert.Equal("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", user.Id);
    }

    [Fact]
    public void GetCurrentUser_FallsBackToNameIdentifier_WhenOidAbsent()
    {
        // Mock auth and some non-Entra schemes don't emit "oid"; NameIdentifier is the fallback.
        var principal = Authenticated(new Claim(ClaimTypes.NameIdentifier, "name-id-only"));
        var service = CreateService(principal);

        var user = service.GetCurrentUser();

        Assert.Equal("name-id-only", user.Id);
    }

    [Fact]
    public void GetCurrentUser_WhenNoSupportedIdClaim_ReturnsNullId()
    {
        var principal = Authenticated(new Claim(ClaimTypes.Name, "Some Display Name"));
        var service = CreateService(principal);

        var user = service.GetCurrentUser();

        Assert.Null(user.Id);
    }
}
