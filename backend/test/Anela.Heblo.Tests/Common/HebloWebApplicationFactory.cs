using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Http;
using Anela.Heblo.API;
using Anela.Heblo.Persistence;
using Anela.Heblo.API.Infrastructure.Authentication;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Microsoft.Extensions.Hosting;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;

namespace Anela.Heblo.Tests.Common;

/// <summary>
/// Base WebApplicationFactory for all integration tests.
/// Provides consistent configuration for Test environment, InMemory database, and Mock authentication.
/// Explicitly loads appsettings.json and appsettings.Test.json to ensure proper configuration in tests.
/// </summary>
public class HebloWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName;

    public HebloWebApplicationFactory()
    {
        _databaseName = $"TestDb_{Guid.NewGuid()}";
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Use Test environment - automatically loads appsettings.Test.json with mock authentication
        builder.UseEnvironment("Test");

        builder.ConfigureServices(services =>
        {
            // Remove the existing DbContext registration
            var dbContextDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
            if (dbContextDescriptor != null)
            {
                services.Remove(dbContextDescriptor);
            }

            // Add InMemory database with unique name for each test class instance
            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseInMemoryDatabase(_databaseName);
            });

            // Remove any existing TelemetryClient registration
            var telemetryClientDescriptor = services.SingleOrDefault(
                s => s.ServiceType == typeof(TelemetryClient));
            if (telemetryClientDescriptor != null)
            {
                services.Remove(telemetryClientDescriptor);
            }

            // Add mock TelemetryClient for test environment
            services.AddSingleton<TelemetryClient>(serviceProvider =>
            {
                var config = new TelemetryConfiguration();
                return new TelemetryClient(config);
            });

            // Register mock E2E testing services for test environment
            // These are needed for E2ETestController controller resolution tests
            services.AddScoped<IServicePrincipalTokenValidator, MockServicePrincipalTokenValidator>();
            services.AddScoped<IE2ESessionService, MockE2ESessionService>();

            // Remove Hangfire background server – it sets Hangfire's static LogProvider.CurrentLogProvider
            // to this factory's ILoggerFactory. When multiple factories run in parallel and one disposes
            // its ILoggerFactory, the stale static reference causes ObjectDisposedException when
            // another factory's tests try to resolve services that touch Hangfire's log provider.
            var hangfireServerDescriptors = services
                .Where(d => d.ServiceType == typeof(IHostedService) &&
                            d.ImplementationType?.Name.Contains("BackgroundJobServer") == true)
                .ToList();
            foreach (var d in hangfireServerDescriptors)
            {
                services.Remove(d);
            }

            // Replace IBackgroundJobClient with a no-op mock to prevent Hangfire's global state
            // (JobStorage.Current, LogProvider.CurrentLogProvider) from being touched during DI resolution.
            var backgroundJobClientDescriptor = services.FirstOrDefault(
                d => d.ServiceType == typeof(IBackgroundJobClient));
            if (backgroundJobClientDescriptor != null)
            {
                services.Remove(backgroundJobClientDescriptor);
            }
            services.AddSingleton<IBackgroundJobClient>(new MockBackgroundJobClient());

            // Replace GraphOneDriveService with MockOneDriveService in tests.
            // GraphOneDriveService requires ITokenAcquisition which is not available when
            // mock authentication is active (UseMockAuth=true in appsettings.Test.json).
            var oneDriveDescriptors = services.Where(s => s.ServiceType == typeof(IOneDriveService)).ToList();
            foreach (var d in oneDriveDescriptors)
                services.Remove(d);
            services.AddScoped<IOneDriveService, MockOneDriveService>();

            // Apply any additional service configuration from derived classes
            ConfigureTestServices(services);
        });

        // Apply any additional web host configuration from derived classes  
        ConfigureTestWebHost(builder);

        base.ConfigureWebHost(builder);
    }

    /// <summary>
    /// Override this method in derived classes to configure additional test services.
    /// </summary>
    protected virtual void ConfigureTestServices(IServiceCollection services)
    {
        // Derived classes can override to add additional service configuration
    }

    /// <summary>
    /// Override this method in derived classes to configure additional web host settings.
    /// </summary>
    protected virtual void ConfigureTestWebHost(IWebHostBuilder builder)
    {
        // Derived classes can override to add additional web host configuration
    }

    /// <summary>
    /// Creates a new scope and seeds the database with test data.
    /// </summary>
    public async Task SeedDatabaseAsync(Func<ApplicationDbContext, Task> seedAction)
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync();
        await seedAction(context);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Clears all data from the in-memory database.
    /// </summary>
    public async Task ClearDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();
    }
}

/// <summary>
/// Mock implementation of IServicePrincipalTokenValidator for testing
/// </summary>
public class MockServicePrincipalTokenValidator : IServicePrincipalTokenValidator
{
    public Task<bool> ValidateAsync(string token)
    {
        // In test environment, always return true for any token
        return Task.FromResult(true);
    }
}

/// <summary>
/// Mock implementation of IE2ESessionService for testing
/// </summary>
public class MockE2ESessionService : IE2ESessionService
{
    public Task CreateE2EAuthenticationSessionAsync(HttpContext httpContext, string environmentName)
    {
        // In test environment, do nothing - mock authentication is handled by MockAuthenticationHandler
        return Task.CompletedTask;
    }

    public System.Security.Claims.Claim[] CreateSyntheticUserClaims(string environmentName)
    {
        // Return basic test claims
        return new[]
        {
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "test-user-id"),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "Test User"),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Email, "test@anela-heblo.com")
        };
    }
}

/// <summary>
/// No-op implementation of IBackgroundJobClient for test environments.
/// Replaces Hangfire's real client to prevent Hangfire's static LogProvider.CurrentLogProvider
/// from being set to a factory-specific ILoggerFactory that may be disposed while other
/// test factories are still running (parallel test class execution).
/// </summary>
public class MockBackgroundJobClient : IBackgroundJobClient
{
    public string Create(Job job, IState state) => "mock-job-id";

    public bool ChangeState(string jobId, IState state, string expectedStateName) => true;
}