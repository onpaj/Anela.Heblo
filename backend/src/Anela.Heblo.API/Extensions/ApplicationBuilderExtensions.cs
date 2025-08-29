using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using HealthChecks.UI.Client;
using Anela.Heblo.Domain.Features.Configuration;

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

        app.UseHttpsRedirection();

        // Built-in HTTP request logging
        app.UseHttpLogging();

        // Use CORS
        app.UseCors(ConfigurationConstants.CORS_POLICY_NAME);

        app.UseAuthentication();
        app.UseAuthorization();

        // Serve static files from wwwroot
        app.UseStaticFiles();

        // If not in development, also use SPA static files
        if (!app.Environment.IsDevelopment())
        {
            app.UseSpaStaticFiles();
        }

        app.MapControllers();

        app.ConfigureHealthCheckEndpoints();

        app.ConfigureSpaFallback();

        return app;
    }

    public static WebApplication ConfigureHealthCheckEndpoints(this WebApplication app)
    {
        // Map health check endpoints
        app.MapHealthChecks("/health");
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ConfigurationConstants.DB_TAG),
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
        });
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,  // Only app liveness, no dependencies
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
        });

        return app;
    }

    public static WebApplication ConfigureSpaFallback(this WebApplication app)
    {
        // SPA fallback - must be after MapControllers
        if (!app.Environment.IsDevelopment())
        {
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
            });
        }

        return app;
    }
}