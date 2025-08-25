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
        ILoggerFactory logger, UrlEncoder encoder, ISystemClock clock)
        : base(options, logger, encoder, clock)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
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
            new Claim(ClaimTypes.Role, AuthorizationConstants.Roles.FinanceReader), // Finance reader role for testing
            new Claim("scp", "access_as_user"), // Scopes
            // Add permission claims for testing
            new Claim("permission", "FinancialOverview.View"),
            // Add claims for product margins authorization
            new Claim("auth_scheme", "MockAuthentication"), // Identifier for mock auth
            new Claim("role", "FinancialManager"), // Mock role for product margins access
            new Claim("department", "finance"), // Department claim for authorization
            new Claim("clearance", "confidential") // Clearance level for detailed margins
        };

        var identity = new ClaimsIdentity(claims, "MockAuthentication");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "MockAuthentication");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}