using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;
using Microsoft.Identity.Abstractions;
using Microsoft.Identity.Web.Resource;
using Microsoft.Extensions.FileProviders;
using Anela.Heblo.API.Authentication;

namespace Anela.Heblo.API;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        var useMockAuth = builder.Configuration.GetValue<bool>("UseMockAuth");
        
        // Only use mock auth if explicitly configured or in specific development environments
        if (builder.Environment.EnvironmentName == "Development" || 
            builder.Environment.EnvironmentName == "Automation")
        {
            useMockAuth = true;
        }
        
        // In Production environment, NEVER use mock auth unless explicitly forced
        if (builder.Environment.EnvironmentName == "Production")
        {
            useMockAuth = false;
        }

        // Log authentication mode for debugging
        Console.WriteLine($"🔐 Environment: {builder.Environment.EnvironmentName}");
        Console.WriteLine($"🔐 Using Mock Authentication: {useMockAuth}");
                         
        if (useMockAuth)
        {
            // Mock authentication for development and automation
            Console.WriteLine("🔓 Configuring Mock Authentication for development/testing");
            builder.Services.AddAuthentication("Mock")
                .AddScheme<MockAuthenticationSchemeOptions, MockAuthenticationHandler>("Mock", _ => { });
            builder.Services.AddAuthorization();
        }
        else
        {
            // Real Microsoft Identity authentication
            Console.WriteLine("🔒 Configuring Microsoft Identity Authentication for production");
            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"))
                .EnableTokenAcquisitionToCallDownstreamApi()
                .AddMicrosoftGraph(builder.Configuration.GetSection("DownstreamApi"))
                .AddInMemoryTokenCaches();
        }

        // Add CORS
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowFrontend", policy =>
            {
                policy.WithOrigins(allowedOrigins)
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .AllowCredentials();
            });
        });

        builder.Services.AddControllers();

        // Add health checks
        builder.Services.AddHealthChecks();

        // Add SPA static files support
        builder.Services.AddSpaStaticFiles(configuration =>
        {
            configuration.RootPath = "wwwroot";
        });

        // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        builder.Services.AddOpenApiDocument();

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
            app.UseOpenApi();
        }

        app.UseHttpsRedirection();

        // Use CORS
        app.UseCors("AllowFrontend");

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

        // Map health check endpoint
        app.MapHealthChecks("/health");

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

        app.Run();
    }
}