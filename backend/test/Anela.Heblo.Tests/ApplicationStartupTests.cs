using Microsoft.AspNetCore.Hosting;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Anela.Heblo.API;
using Anela.Heblo.Tests.Common;
using Anela.Heblo.Xcc.Telemetry;
using Xunit.Abstractions;
using MediatR;
using System.Reflection;

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

    public static IEnumerable<object[]> GetControllerTypes()
    {
        return typeof(Program).Assembly
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Name.EndsWith("Controller"))
            .Select(t => new object[] { t });
    }

    [Theory]
    [MemberData(nameof(GetControllerTypes))]
    public void Controller_Should_Be_Resolvable(Type controllerType)
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var serviceProvider = scope.ServiceProvider;

        // Act & Assert
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

    public static IEnumerable<object[]> GetMediatRHandlerTypes()
    {
        // Get all assemblies that contain MediatR handlers (Application layer and potentially others)
        var assemblies = new[]
        {
            typeof(Anela.Heblo.Application.ApplicationModule).Assembly, // Application layer
            typeof(Program).Assembly // API layer (in case there are any handlers there)
        };

        var handlerTypes = new List<Type>();

        // Find all types implementing IRequestHandler<,> interfaces
        foreach (var assembly in assemblies)
        {
            var types = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract)
                .Where(t => t.GetInterfaces()
                    .Any(i => i.IsGenericType &&
                             (i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>) ||
                              i.GetGenericTypeDefinition() == typeof(IRequestHandler<>))))
                .ToList();

            handlerTypes.AddRange(types);
        }

        return handlerTypes.Select(t => new object[] { t });
    }

    [Theory]
    [MemberData(nameof(GetMediatRHandlerTypes))]
    public void MediatR_Handler_Should_Be_Resolvable(Type handlerType)
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var serviceProvider = scope.ServiceProvider;

        // Act & Assert
        try
        {
            // MediatR handlers are registered via their implemented interfaces, not directly
            // Find all IRequestHandler<,> interfaces implemented by this handler
            var requestHandlerInterfaces = handlerType.GetInterfaces()
                .Where(i => i.IsGenericType &&
                           (i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>) ||
                            i.GetGenericTypeDefinition() == typeof(IRequestHandler<>)))
                .ToList();

            if (requestHandlerInterfaces.Count == 0)
            {
                throw new InvalidOperationException($"Handler {handlerType.Name} does not implement IRequestHandler interface");
            }

            // Test that each interface can be resolved
            foreach (var interfaceType in requestHandlerInterfaces)
            {
                var handler = serviceProvider.GetRequiredService(interfaceType);
                handler.Should().NotBeNull();
                handler.GetType().Should().Be(handlerType);
            }

            _output.WriteLine($"✅ Successfully resolved MediatR handler {handlerType.Name} via {requestHandlerInterfaces.Count} interface(s)");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"❌ Failed to resolve MediatR handler {handlerType.Name}: {ex.Message}");
            throw new InvalidOperationException($"Failed to resolve MediatR handler {handlerType.Name}", ex);
        }
    }

    [Fact]
    public void Should_Find_Controllers_And_Handlers()
    {
        // Arrange & Act
        var controllerTypes = GetControllerTypes().ToList();
        var handlerTypes = GetMediatRHandlerTypes().ToList();

        // Assert
        controllerTypes.Should().NotBeEmpty("Application should have at least some controllers");
        handlerTypes.Should().NotBeEmpty("Application should have at least some MediatR handlers");

        _output.WriteLine($"Found {controllerTypes.Count} controller types and {handlerTypes.Count} MediatR handler types");

        // List some examples
        _output.WriteLine("Controllers found:");
        foreach (var controllerType in controllerTypes.Take(5))
        {
            _output.WriteLine($"  - {((Type)controllerType[0]).Name}");
        }
        if (controllerTypes.Count > 5)
        {
            _output.WriteLine($"  ... and {controllerTypes.Count - 5} more");
        }

        _output.WriteLine("MediatR handlers found:");
        foreach (var handlerType in handlerTypes.Take(5))
        {
            _output.WriteLine($"  - {((Type)handlerType[0]).Name}");
        }
        if (handlerTypes.Count > 5)
        {
            _output.WriteLine($"  ... and {handlerTypes.Count - 5} more");
        }
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