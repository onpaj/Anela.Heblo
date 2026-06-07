using System.Security.Claims;
using System.Text.Encodings.Web;
using Anela.Heblo.Domain.Features.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.API.Infrastructure.Authentication;

public class MockAuthenticationSchemeOptions : AuthenticationSchemeOptions
{
}

public class MockAuthenticationHandler : AuthenticationHandler<MockAuthenticationSchemeOptions>
{
    public MockAuthenticationHandler(IOptionsMonitor<MockAuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identityClaims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "mock-user-id"),
            new Claim(ClaimTypes.Name, "Mock User"),
            new Claim(ClaimTypes.Email, "mock@anela-heblo.com"),
            new Claim("preferred_username", "mock@anela-heblo.com"),
            new Claim("name", "Mock User"),
            new Claim("given_name", "Mock"),
            new Claim("family_name", "User"),
            // Add Entra ID specific claims
            new Claim("oid", "00000000-0000-0000-0000-000000000000"), // Object ID
            new Claim("tid", "11111111-1111-1111-1111-111111111111"), // Tenant ID
            new Claim("scp", "access_as_user"), // Scopes
        };

        var roleClaims = new[] { AccessMatrix.BaseRole }
            .Concat(AccessMatrix.AllRoleValues())
            .Select(r => new Claim(ClaimTypes.Role, r));

        var claims = identityClaims.Concat(roleClaims).ToArray();
        var identity = new ClaimsIdentity(claims, "Mock");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Mock");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}