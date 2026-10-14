using System.Globalization;
using System.Threading.RateLimiting;
using Anela.Heblo.API.Infrastructure.Authentication;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Identity.Web;

namespace Anela.Heblo.API.MCP;

/// <summary>Per-service-principal rate limit on /mcp (spec 7.3). App-only callers (agents) get a
/// fixed one-minute window keyed by their oid (the E2E test session counts as app-only); people
/// (delegated tokens, web-app cookie sessions) are not limited.</summary>
public static class McpAgentRateLimiting
{
    public const string PolicyName = "mcp-agent";
    public const string PermitLimitConfigKey = "Ads:AgentRateLimitPerMinute";
    public const int DefaultPermitLimit = 60;

    private const string InteractivePartitionKey = "interactive";
    private const string AppPartitionPrefix = "app:";
    private const string UnknownObjectId = "unknown";
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public static IServiceCollection AddMcpAgentRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var permitLimit = configuration.GetValue(PermitLimitConfigKey, DefaultPermitLimit);
        if (permitLimit <= 0)
        {
            throw new InvalidOperationException(
                $"{PermitLimitConfigKey} must be a positive number of requests per minute (got {permitLimit}).");
        }

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                ApplyRetryAfter(context.HttpContext.Response, context.Lease);
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger(typeof(McpAgentRateLimiting).FullName!);
                logger.LogWarning(
                    "MCP agent rate limit hit for service principal {ObjectId} ({PermitLimit}/min)",
                    context.HttpContext.User.GetObjectId() ?? UnknownObjectId,
                    permitLimit);
                return ValueTask.CompletedTask;
            };
            options.AddPolicy(PolicyName, httpContext => GetPartition(httpContext, permitLimit));
        });

        return services;
    }

    /// <summary>Sets <c>Retry-After</c> (whole seconds, rounded up) when the rejected lease says when to retry.</summary>
    public static void ApplyRetryAfter(HttpResponse response, RateLimitLease lease)
    {
        if (lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            var seconds = (int)Math.Ceiling(retryAfter.TotalSeconds);
            response.Headers.RetryAfter = Math.Max(1, seconds).ToString(CultureInfo.InvariantCulture);
        }
    }

    public static RateLimitPartition<string> GetPartition(HttpContext httpContext, int permitLimit)
    {
        if (!TokenClientClaims.IsAppOnly(httpContext))
        {
            return RateLimitPartition.GetNoLimiter(InteractivePartitionKey);
        }

        var objectId = httpContext.User.GetObjectId() ?? UnknownObjectId;
        return RateLimitPartition.GetFixedWindowLimiter(
            AppPartitionPrefix + objectId,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = Window,
                QueueLimit = 0,
                AutoReplenishment = true,
            });
    }
}
