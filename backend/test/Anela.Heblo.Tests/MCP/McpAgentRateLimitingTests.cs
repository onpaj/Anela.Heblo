using System.Security.Claims;
using System.Threading.RateLimiting;
using Anela.Heblo.API.Infrastructure.Authentication;
using Anela.Heblo.API.MCP;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Anela.Heblo.Tests.MCP;

public class McpAgentRateLimitingTests
{
    private const int PermitLimit = 60;
    private const string Bearer = "Bearer eyJ0eXAi.fake.token";

    private static HttpContext Context(string? authorizationHeader, params Claim[] claims) =>
        Context(authorizationHeader, "TestAuth", claims);

    private static HttpContext Context(string? authorizationHeader, string authenticationType, params Claim[] claims)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType, "name", "roles")),
        };
        if (authorizationHeader is not null)
        {
            context.Request.Headers.Authorization = authorizationHeader;
        }

        return context;
    }

    private static HttpContext AgentContext(string oid) =>
        Context(Bearer, new Claim("oid", oid), new Claim("azp", "agent-client"), new Claim("roles", "Marketing.Agent"));

    private static int CountAcquired(HttpContext context, int attempts)
    {
        var partition = McpAgentRateLimiting.GetPartition(context, PermitLimit);
        using var limiter = partition.Factory(partition.PartitionKey);
        return Enumerable.Range(0, attempts).Count(_ => limiter.AttemptAcquire().IsAcquired);
    }

    [Fact]
    public void AppOnlyCaller_IsPartitionedByObjectId()
    {
        var partition = McpAgentRateLimiting.GetPartition(AgentContext("agent-1"), PermitLimit);

        partition.PartitionKey.Should().Be("app:agent-1");
    }

    [Fact]
    public void TwoAgents_GetSeparateBuckets()
    {
        var first = McpAgentRateLimiting.GetPartition(AgentContext("agent-1"), PermitLimit);
        var second = McpAgentRateLimiting.GetPartition(AgentContext("agent-2"), PermitLimit);

        first.PartitionKey.Should().NotBe(second.PartitionKey);
    }

    [Fact]
    public void AppOnlyCaller_IsRefusedAfterPermitLimitWithinTheWindow()
    {
        CountAcquired(AgentContext("agent-1"), PermitLimit + 1).Should().Be(PermitLimit);
    }

    [Fact]
    public void DelegatedUser_IsNeverLimited()
    {
        var user = Context(Bearer, new Claim("oid", "user-1"), new Claim("azp", "api-client"), new Claim("scp", "access_as_user"));

        McpAgentRateLimiting.GetPartition(user, PermitLimit).PartitionKey.Should().Be("interactive");
        CountAcquired(user, PermitLimit * 10).Should().Be(PermitLimit * 10);
    }

    [Fact]
    public void WebAppCookieSession_IsNeverLimited()
    {
        var cookieUser = Context(authorizationHeader: null, new Claim("oid", "user-oid"), new Claim("scp", "access_as_user"));

        McpAgentRateLimiting.GetPartition(cookieUser, PermitLimit).PartitionKey.Should().Be("interactive");
    }

    [Fact]
    public void E2ESession_IsLimitedPerObjectId_AsAServiceIdentity()
    {
        var e2eUser = Context(
            authorizationHeader: null, E2ESessionService.AuthenticationType,
            new Claim("oid", "e2e-test-object-id"), new Claim("scp", "access_as_user"));

        McpAgentRateLimiting.GetPartition(e2eUser, PermitLimit).PartitionKey.Should().Be("app:e2e-test-object-id");
    }

    [Fact]
    public void AppOnlyCallerWithoutOid_SharesTheUnknownBucket()
    {
        var anonymousApp = Context(Bearer, new Claim("azp", "agent-client"));

        McpAgentRateLimiting.GetPartition(anonymousApp, PermitLimit).PartitionKey.Should().Be("app:unknown");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public void AddMcpAgentRateLimiting_RejectsNonPositiveLimit(string configured)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [McpAgentRateLimiting.PermitLimitConfigKey] = configured })
            .Build();

        var act = () => new ServiceCollection().AddMcpAgentRateLimiting(configuration);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Ads:AgentRateLimitPerMinute*");
    }

    [Fact]
    public void ApplyRetryAfter_SetsTheHeaderInWholeSeconds_FromTheRejectedLease()
    {
        // Arrange — a real fixed-window rejection carries RetryAfter metadata
        var partition = McpAgentRateLimiting.GetPartition(AgentContext("agent-1"), permitLimit: 1);
        using var limiter = partition.Factory(partition.PartitionKey);
        using var granted = limiter.AttemptAcquire();
        using var rejected = limiter.AttemptAcquire();
        var response = new DefaultHttpContext().Response;

        // Act
        McpAgentRateLimiting.ApplyRetryAfter(response, rejected);

        // Assert
        rejected.IsAcquired.Should().BeFalse();
        var seconds = int.Parse(response.Headers.RetryAfter.ToString());
        seconds.Should().BeInRange(1, 60);
    }

    [Fact]
    public void ApplyRetryAfter_LeavesTheHeaderUnset_WhenTheLeaseHasNoRetryAfter()
    {
        var response = new DefaultHttpContext().Response;

        McpAgentRateLimiting.ApplyRetryAfter(response, new NoMetadataLease());

        response.Headers.ContainsKey("Retry-After").Should().BeFalse();
    }

    private sealed class NoMetadataLease : RateLimitLease
    {
        public override bool IsAcquired => false;
        public override IEnumerable<string> MetadataNames => Array.Empty<string>();
        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            metadata = null;
            return false;
        }
    }
}
