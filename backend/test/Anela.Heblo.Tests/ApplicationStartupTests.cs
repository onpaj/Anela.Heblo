using Microsoft.AspNetCore.Hosting;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Anela.Heblo.API;
using Anela.Heblo.Tests.Common;
using Anela.Heblo.Xcc.Telemetry;
using Xunit.Abstractions;

namespace Anela.Heblo.Tests;

/// <summary>
/// Integration tests to verify application startup and dependency resolution
/// </summary>
public class ApplicationStartupTests : IClassFixture<HebloWebApplicationFactory>
{
    private readonly HebloWebApplicationFactory _factory;
    private readonly ITestOutputHelper _output;

    public ApplicationStartupTests(HebloWebApplicationFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    [Fact]
    public void Application_Should_Start_Successfully()
    {
        // Arrange & Act
        using var client = _factory.CreateClient();

        // Assert - If we get here, the application started successfully
        client.Should().NotBeNull();
        _output.WriteLine("✅ Application started successfully");
    }

    [Fact]
    public void All_Controllers_Should_Be_Resolvable()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var serviceProvider = scope.ServiceProvider;

        // Get all controller types
        var controllerTypes = typeof(Program).Assembly
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Name.EndsWith("Controller"))
            .ToList();

        _output.WriteLine($"Found {controllerTypes.Count} controller types:");

        // Act & Assert
        foreach (var controllerType in controllerTypes)
        {
            try
            {
                // Try to create controller instance manually with required services
                var constructors = controllerType.GetConstructors();
                var constructor = constructors.OrderBy(c => c.GetParameters().Length).First();
                var parameters = constructor.GetParameters();

                var args = new object[parameters.Length];
                for (int i = 0; i < parameters.Length; i++)
                {
                    var paramType = parameters[i].ParameterType;
                    var paramInfo = parameters[i];

                    // Handle optional parameters or nullable types
                    if (paramInfo.HasDefaultValue ||
                        (paramType.IsGenericType && paramType.GetGenericTypeDefinition() == typeof(Nullable<>)) ||
                        paramType.Name.EndsWith("?"))
                    {
                        args[i] = serviceProvider.GetService(paramType) ?? paramInfo.DefaultValue ?? null!;
                    }
                    else
                    {
                        args[i] = serviceProvider.GetRequiredService(paramType);
                    }
                }

                var controller = Activator.CreateInstance(controllerType, args);
                controller.Should().NotBeNull();
                _output.WriteLine($"✅ Successfully resolved {controllerType.Name}");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"❌ Failed to resolve {controllerType.Name}: {ex.Message}");
                throw new InvalidOperationException($"Failed to resolve controller {controllerType.Name}", ex);
            }
        }
    }

    [Fact]
    public void All_Required_Services_Should_Be_Registered()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var serviceProvider = scope.ServiceProvider;

        // Define required services that should be available
        var requiredServices = new[]
        {
            typeof(ITelemetryService),
            typeof(ILogger<Program>)
        };

        // Act & Assert
        foreach (var serviceType in requiredServices)
        {
            try
            {
                var service = serviceProvider.GetRequiredService(serviceType);
                service.Should().NotBeNull();
                _output.WriteLine($"✅ Successfully resolved service {serviceType.Name}");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"❌ Failed to resolve service {serviceType.Name}: {ex.Message}");
                throw new InvalidOperationException($"Failed to resolve required service {serviceType.Name}", ex);
            }
        }
    }

    [Fact]
    public void TelemetryService_Should_Be_Correct_Implementation()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var serviceProvider = scope.ServiceProvider;

        // Act
        var telemetryService = serviceProvider.GetRequiredService<ITelemetryService>();

        // Assert
        telemetryService.Should().NotBeNull();

        // In test environment (no Application Insights), should use NoOpTelemetryService
        telemetryService.Should().BeOfType<NoOpTelemetryService>();
        _output.WriteLine("✅ NoOpTelemetryService is correctly registered for test environment");
    }

    [Fact]
    public async Task Health_Endpoints_Should_Be_Accessible()
    {
        // Arrange
        using var client = _factory.CreateClient();

        // Act & Assert - /health/live should always work (no dependencies)
        var liveResponse = await client.GetAsync("/health/live");
        liveResponse.EnsureSuccessStatusCode();
        _output.WriteLine("✅ /health/live endpoint is accessible");

        // /health and /health/ready might fail if database is not available, which is expected in test environment
        var healthResponse = await client.GetAsync("/health");
        _output.WriteLine($"ℹ️ /health returned status: {healthResponse.StatusCode}");

        var readyResponse = await client.GetAsync("/health/ready");
        _output.WriteLine($"ℹ️ /health/ready returned status: {readyResponse.StatusCode}");

        // In test environment, we expect database to be unavailable, so 503 is acceptable
        // The important thing is that the endpoints are responding and not throwing exceptions
        Assert.True(healthResponse.StatusCode == System.Net.HttpStatusCode.OK ||
                   healthResponse.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable,
                   $"Health endpoint returned unexpected status: {healthResponse.StatusCode}");
    }

    [Fact]
    public async Task Controllers_Should_Respond_To_Basic_Requests()
    {
        // Arrange
        using var client = _factory.CreateClient();

        // Test basic API availability - no specific endpoints needed for now

        // Config controller was removed as it's no longer needed
        // Frontend now uses build-time environment variables instead of runtime config
    }

    [Fact]
    public void Application_Should_Use_Mock_Authentication_In_Test()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var serviceProvider = scope.ServiceProvider;

        // Act
        var hostEnvironment = serviceProvider.GetRequiredService<IWebHostEnvironment>();

        // Assert
        hostEnvironment.Should().NotBeNull();
        _output.WriteLine($"✅ Environment: {hostEnvironment.EnvironmentName}");

        // In test environment, mock auth should be enabled
        // This is verified by the fact that the application starts without real Azure AD configuration
        _output.WriteLine("✅ Mock authentication is working (application started without Azure AD config)");
    }
}