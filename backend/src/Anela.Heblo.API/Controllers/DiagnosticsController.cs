using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.ApplicationInsights;

namespace Anela.Heblo.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DiagnosticsController : ControllerBase
{
    private readonly ILogger<DiagnosticsController> _logger;
    private readonly TelemetryClient _telemetryClient;
    private readonly IWebHostEnvironment _environment;

    public DiagnosticsController(ILogger<DiagnosticsController> logger, TelemetryClient telemetryClient, IWebHostEnvironment environment)
    {
        _logger = logger;
        _telemetryClient = telemetryClient;
        _environment = environment;
    }

    private bool TryBlockInProduction(out IActionResult result)
    {
        if (_environment.IsProduction())
        {
            result = NotFound();
            return true;
        }

        result = null!;
        return false;
    }

    [HttpGet("test-logging")]
    public IActionResult TestLogging()
    {
        if (TryBlockInProduction(out var blocked)) return blocked;

        _logger.LogInformation("Test Information log from Diagnostics endpoint");
        _logger.LogWarning("Test Warning log from Diagnostics endpoint");
        _logger.LogError("Test Error log from Diagnostics endpoint");

        // Custom telemetry events
        _telemetryClient.TrackEvent("DiagnosticsTestEvent", new Dictionary<string, string>
        {
            ["TestType"] = "Logging",
            ["Timestamp"] = DateTimeOffset.UtcNow.ToString("O"),
            ["Environment"] = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Unknown"
        });

        // Custom metric
        _telemetryClient.TrackMetric("TestMetric", 42.0, new Dictionary<string, string>
        {
            ["MetricType"] = "DiagnosticTest"
        });

        return Ok(new
        {
            Message = "Logging test completed",
            Timestamp = DateTimeOffset.UtcNow,
            Environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
            ApplicationInsightsConfigured = !string.IsNullOrEmpty(_telemetryClient.InstrumentationKey)
        });
    }

    [HttpGet("test-exception")]
    public IActionResult TestException()
    {
        if (TryBlockInProduction(out var blocked)) return blocked;

        try
        {
            throw new InvalidOperationException("This is a test exception for Application Insights");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Test exception occurred in Diagnostics endpoint");

            // Track exception in Application Insights
            _telemetryClient.TrackException(ex, new Dictionary<string, string>
            {
                ["TestType"] = "Exception",
                ["Endpoint"] = "DiagnosticsController.TestException"
            });

            return StatusCode(500, new
            {
                Message = "Test exception thrown and logged",
                ExceptionType = ex.GetType().Name,
                ExceptionMessage = ex.Message
            });
        }
    }

    [HttpGet("health")]
    public IActionResult Health()
    {
        if (TryBlockInProduction(out var blocked)) return blocked;

        _logger.LogInformation("Health check endpoint called");

        return Ok(new
        {
            Status = "Healthy",
            Timestamp = DateTimeOffset.UtcNow,
            Version = "1.0.0",
            Environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
            MachineName = Environment.MachineName
        });
    }

    [HttpGet("appinsights-config")]
    public IActionResult GetApplicationInsightsConfig()
    {
        if (TryBlockInProduction(out var blocked)) return blocked;

        var connectionString = Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING")
                            ?? Environment.GetEnvironmentVariable("APPINSIGHTS_INSTRUMENTATIONKEY");

        var hasConnectionString = !string.IsNullOrEmpty(connectionString);
        var hasInstrumentationKey = !string.IsNullOrEmpty(_telemetryClient.InstrumentationKey);

        _logger.LogInformation("Application Insights configuration check - HasConnectionString: {HasConnectionString}, HasInstrumentationKey: {HasInstrumentationKey}",
            hasConnectionString, hasInstrumentationKey);

        return Ok(new
        {
            HasConnectionString = hasConnectionString,
            HasInstrumentationKey = hasInstrumentationKey,
            Environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
            CloudRole = _telemetryClient.Context.Cloud.RoleName,
            CloudRoleInstance = _telemetryClient.Context.Cloud.RoleInstance
        });
    }
}