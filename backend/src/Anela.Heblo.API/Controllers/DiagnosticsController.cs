using Microsoft.AspNetCore.Mvc;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;

namespace Anela.Heblo.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DiagnosticsController : ControllerBase
{
    private readonly ILogger<DiagnosticsController> _logger;
    private readonly TelemetryClient _telemetryClient;

    public DiagnosticsController(ILogger<DiagnosticsController> logger, TelemetryClient telemetryClient)
    {
        _logger = logger;
        _telemetryClient = telemetryClient;
    }

    [HttpGet("test-logging")]
    public IActionResult TestLogging()
    {
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
}