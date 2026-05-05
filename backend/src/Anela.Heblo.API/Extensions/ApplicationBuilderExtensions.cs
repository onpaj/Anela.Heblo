using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using HealthChecks.UI.Client;
using Anela.Heblo.Domain.Features.Configuration;
using Hangfire;
using Anela.Heblo.API.Infrastructure.Hangfire;
using Microsoft.AspNetCore.HttpLogging;
using Anela.Heblo.API.Infrastructure.Authentication;
using Anela.Heblo.API.MCP;
using ModelContextProtocol.AspNetCore;

namespace Anela.Heblo.API.Extensions;

public static class ApplicationBuilderExtensions
{
    public static WebApplication ConfigureApplicationPipeline(this WebApplication app)
    {
        // Configure the HTTP request pipeline
        if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
        {
            app.UseSwagger();
            app.UseSwaggerUI(options =>
            {
                // Only configure OAuth2 for Swagger UI if mock auth is disabled
                var useMockAuth = app.Configuration.GetValue<bool>("UseMockAuth", false);

                if (!useMockAuth)
                {
                    // Configure OAuth2 for Swagger UI
                    var azureAdConfig = app.Configuration.GetSection("AzureAd");
                    var clientId = azureAdConfig["ClientId"];
                    var scopes = azureAdConfig["Scopes"];

                    if (!string.IsNullOrEmpty(clientId))
                    {
                        // OAuth2 configuration for Swagger UI
                        options.OAuthClientId(clientId);
                        options.OAuthUsePkce();
                        options.OAuthScopeSeparator(" ");
                        options.OAuthAppName("Anela Heblo API");

                        // Additional OAuth2 settings for better UX
                        options.OAuthUseBasicAuthenticationWithAccessCodeGrant();

                        // Pre-fill scopes if available
                        if (!string.IsNullOrEmpty(scopes))
                        {
                            var scopeArray = scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            options.OAuthScopes(scopeArray);
                        }
                        else
                        {
                            // Fallback to API scope
                            options.OAuthScopes($"api://{clientId}/access_as_user");
                        }
                    }
                }

                // Enable additional Swagger UI features (always)
                options.EnableDeepLinking();
                options.DisplayRequestDuration();
            });
            app.UseOpenApi();
        }

        // Configure forwarded headers for deployment behind load balancer/proxy (HTTPS handling)
        app.UseForwardedHeaders();

        // Use CORS - MUST be before UseHttpsRedirection to ensure CORS headers are included in redirect responses
        app.UseCors(ConfigurationConstants.CORS_POLICY_NAME);

        app.UseHttpsRedirection();

        // Built-in HTTP request logging
        app.UseHttpLogging();

        // Request logging middleware - detailed logging for diagnostics
        // Must be after CORS and before authentication to capture all request details
        app.UseRequestLogging();

        if (E2ETestAuthenticationMiddleware.ShouldBeRegistered(app))
        {
            app.UseMiddleware<E2ETestAuthenticationMiddleware>();
        }

        // Routing must be explicitly configured before authentication/authorization
        app.UseRouting();

        app.UseAuthentication();
        app.UseAuthorization();

        // Request timeouts middleware — must be after auth so timeout policies can be applied per-endpoint
        app.UseRequestTimeouts();

        // Add Hangfire authentication middleware before Hangfire dashboard
        // This middleware handles redirects to login when user is not authenticated
        app.UseMiddleware<HangfireAuthenticationMiddleware>();

        // Configure Hangfire dashboard with custom token authorization filter
        // This filter properly handles Bearer tokens and respects UseMockAuth configuration
        var authFilter = app.Services.GetRequiredService<HangfireDashboardTokenAuthorizationFilter>();
        app.MapHangfireDashboard("/hangfire", new DashboardOptions()
        {
            Authorization = new[] { authFilter }
        });


        // Serve static files from wwwroot
        app.UseStaticFiles();

        // If not in development, also use SPA static files
        if (!app.Environment.IsDevelopment())
        {
            app.UseSpaStaticFiles();
        }

        app.MapControllers();

        // MCP bad-request diagnostics — blocks probes without valid Accept header (returns 404)
        // and logs structured context for GET /mcp 400 responses to identify bad clients (#593).
        app.UseMiddleware<McpBadRequestMiddleware>();

        // MCP diagnostics — logs structured context for GET /mcp 404 responses to
        // aid investigation of session-resumption failures (issue #599).
        app.UseMiddleware<McpDiagnosticsMiddleware>();

        // MCP server endpoint — requires authentication (Microsoft Entra ID)
        // 5-minute session timeout terminates zombie SSE connections that linger after client disconnect.
        app.MapMcp("/mcp")
            .RequireAuthorization()
            .WithRequestTimeout(TimeSpan.FromMinutes(5));

        // OAuth 2.0 authorization server metadata — required for MCP clients (e.g. Claude Desktop)
        // to discover the real authorization server (Microsoft Entra ID) instead of hitting the SPA fallback.
        app.MapGet("/.well-known/oauth-authorization-server", (IConfiguration config) =>
        {
            var tenantId = config["AzureAd:TenantId"];
            var clientId = config["AzureAd:ClientId"];
            var baseUrl = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0";

            var scope = $"api://{clientId}/access_as_user";

            return Results.Json(new
            {
                issuer = $"https://login.microsoftonline.com/{tenantId}/v2.0",
                authorization_endpoint = $"{baseUrl}/authorize",
                token_endpoint = $"{baseUrl}/token",
                response_types_supported = new[] { "code" },
                grant_types_supported = new[] { "authorization_code", "refresh_token" },
                code_challenge_methods_supported = new[] { "S256" },
                scopes_supported = new[] { scope, "offline_access" },
                client_id = clientId,
            });
        }).AllowAnonymous();

        app.ConfigureHealthCheckEndpoints();

        app.ConfigureSpaFallback();

        return app;
    }

    public static WebApplication ConfigureHealthCheckEndpoints(this WebApplication app)
    {
        // Map health check endpoints
        app.MapHealthChecks("/health").WithHttpLogging(HttpLoggingFields.None);
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ConfigurationConstants.DB_TAG) || check.Tags.Contains("ready"),
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
        }).WithHttpLogging(HttpLoggingFields.None);
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,  // Only app liveness, no dependencies
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
        }).WithHttpLogging(HttpLoggingFields.None);

        return app;
    }

    public static WebApplication ConfigureSpaFallback(this WebApplication app)
    {
        // SPA fallback - must be after MapControllers
        if (!app.Environment.IsDevelopment())
        {
            // Reject non-GET/HEAD requests before the SPA fallback to avoid a 500 when
            // index.html is absent (e.g. scanners POSTing to /index.html). Standards-
            // compliant clients receive 405 Method Not Allowed instead of an unhandled exception.
            app.Use(async (context, next) =>
            {
                // Only reject non-GET/HEAD when no endpoint matched this request.
                // If an API controller matched, context.GetEndpoint() is non-null here
                // (UseRouting already ran), so we let it through.
                if (context.GetEndpoint() is null &&
                    !HttpMethods.IsGet(context.Request.Method) &&
                    !HttpMethods.IsHead(context.Request.Method))
                {
                    context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                    return;
                }
                await next();
            });

            // Guard: if index.html is absent (e.g. frontend build not deployed), respond with
            // 503 Service Unavailable instead of letting UseSpa throw InvalidOperationException.
            // This prevents the unhandled exception spike seen in App Insights (issue #667).
            var indexHtmlPath = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "index.html");
            if (!File.Exists(indexHtmlPath))
            {
                var logger = app.Services.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Anela.Heblo.API.SpaFallback");
                logger.LogError(
                    "SPA frontend build not found at {IndexHtmlPath}. Skipping UseSpa — " +
                    "all unmatched GET requests will return 503 until the frontend is deployed.",
                    indexHtmlPath);

                // Only intercept GET/HEAD requests with no matched endpoint (SPA fallback paths).
                // API and health endpoints have a matched endpoint at this point, so they
                // pass through via next() — mirroring how the real UseSpa middleware works.
                app.Use(async (context, next) =>
                {
                    if (context.GetEndpoint() is null &&
                        (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)))
                    {
                        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                        context.Response.ContentType = "text/plain; charset=utf-8";
                        await context.Response.WriteAsync(
                            "Service temporarily unavailable: frontend build not deployed.");
                        return;
                    }
                    await next();
                });

                return app;
            }

            app.UseSpa(spa =>
            {
                spa.Options.SourcePath = "wwwroot";
                spa.Options.DefaultPageStaticFileOptions = new StaticFileOptions
                {
                    OnPrepareResponse = context =>
                    {
                        // Prevent caching of index.html
                        context.Context.Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");
                        context.Context.Response.Headers.Append("Pragma", "no-cache");
                        context.Context.Response.Headers.Append("Expires", "0");
                    }
                };

                // IMPORTANT: Configure SPA to NOT intercept API or MCP routes
                spa.ApplicationBuilder.UseRouting();
                spa.ApplicationBuilder.UseEndpoints(endpoints =>
                {
                    endpoints.Map("/api/{**catch-all}", context =>
                    {
                        context.Response.StatusCode = 404;
                        return Task.CompletedTask;
                    });
                    // Prevent SPA from serving index.html for MCP sub-paths (e.g. /mcp/sse)
                    // that are not handled by MapMcp but must not fall through to the SPA.
                    endpoints.Map("/mcp/{**catch-all}", context =>
                    {
                        context.Response.StatusCode = 404;
                        return Task.CompletedTask;
                    });
                });
            });
        }

        return app;
    }

    /// <summary>
    /// Determines if the current request is a health check request.
    /// </summary>
    private static bool IsHealthCheckRequest(HttpContext context)
    {
        var path = context.Request.Path.Value;
        return !string.IsNullOrEmpty(path) && (
            path.StartsWith("/health", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/healthz", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/ready", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/live", StringComparison.OrdinalIgnoreCase)
        );
    }
}

class SuppressHealthHttpLogging : IHttpLoggingInterceptor
{
    public ValueTask OnRequestAsync(HttpLoggingInterceptorContext ctx)
    {
        var path = ctx.HttpContext.Request.Path;
        if (path.StartsWithSegments("/health") ||
            path.StartsWithSegments("/healthz") ||
            path.StartsWithSegments("/health/ready") ||
            path.StartsWithSegments("/health/live"))
        {
            ctx.LoggingFields = HttpLoggingFields.None; // vypnout vše
        }
        return default;
    }

    public ValueTask OnResponseAsync(HttpLoggingInterceptorContext ctx) => default;
}