using Microsoft.AspNetCore.Mvc;
using Microsoft.ApplicationInsights;
using Anela.Heblo.Application.Features.Catalog.Services;

namespace Anela.Heblo.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DiagnosticsController : ControllerBase
{
    private readonly ILogger<DiagnosticsController> _logger;
    private readonly TelemetryClient _telemetryClient;
    private readonly IManufactureCostCalculationService _manufactureCostService;
    private readonly ISalesCostCalculationService _salesCostService;
    private readonly IOverheadCostCalculationService _overheadCostService;

    public DiagnosticsController(
        ILogger<DiagnosticsController> logger,
        TelemetryClient telemetryClient,
        IManufactureCostCalculationService manufactureCostService,
        ISalesCostCalculationService salesCostService,
        IOverheadCostCalculationService overheadCostService)
    {
        _logger = logger;
        _telemetryClient = telemetryClient;
        _manufactureCostService = manufactureCostService;
        _salesCostService = salesCostService;
        _overheadCostService = overheadCostService;
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

    [HttpGet("appinsights-config")]
    public IActionResult GetApplicationInsightsConfig()
    {
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
            InstrumentationKey = hasInstrumentationKey ? _telemetryClient.InstrumentationKey : "Not configured",
            Environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
            ConnectionStringSource = connectionString?.Substring(0, Math.Min(50, connectionString.Length)) + "...",
            CloudRole = _telemetryClient.Context.Cloud.RoleName,
            CloudRoleInstance = _telemetryClient.Context.Cloud.RoleInstance
        });
    }

    [HttpGet("cache-status")]
    public IActionResult GetCacheStatus()
    {
        _logger.LogInformation("Cache status check requested");

        var manufactureCostLoaded = _manufactureCostService.IsLoaded;
        var salesCostLoaded = _salesCostService.IsLoaded;
        var overheadCostLoaded = _overheadCostService.IsLoaded;

        var allLoaded = manufactureCostLoaded && salesCostLoaded && overheadCostLoaded;
        var noneLoaded = !manufactureCostLoaded && !salesCostLoaded && !overheadCostLoaded;

        string overallStatus;
        if (allLoaded)
        {
            overallStatus = "Healthy - All caches loaded";
        }
        else if (noneLoaded)
        {
            overallStatus = "Unhealthy - No caches loaded";
        }
        else
        {
            overallStatus = "Degraded - Some caches not loaded";
        }

        _logger.LogInformation("Cache status: {OverallStatus} - M1: {M1Loaded}, M2: {M2Loaded}, M3: {M3Loaded}",
            overallStatus, manufactureCostLoaded, salesCostLoaded, overheadCostLoaded);

        return Ok(new
        {
            OverallStatus = overallStatus,
            AllCachesLoaded = allLoaded,
            Timestamp = DateTimeOffset.UtcNow,
            Caches = new
            {
                ManufactureCost = new
                {
                    ServiceName = "IManufactureCostCalculationService",
                    IsLoaded = manufactureCostLoaded,
                    Status = manufactureCostLoaded ? "Loaded" : "Not Loaded",
                    Description = "M1 - Production capacity costs (baseline M1_A and actual M1_B)"
                },
                SalesCost = new
                {
                    ServiceName = "ISalesCostCalculationService",
                    IsLoaded = salesCostLoaded,
                    Status = salesCostLoaded ? "Loaded" : "Not Loaded",
                    Description = "M2 - Sales and marketing costs"
                },
                OverheadCost = new
                {
                    ServiceName = "IOverheadCostCalculationService",
                    IsLoaded = overheadCostLoaded,
                    Status = overheadCostLoaded ? "Loaded" : "Not Loaded",
                    Description = "M3 - Overhead and administrative costs"
                }
            },
            Recommendations = GetCacheRecommendations(manufactureCostLoaded, salesCostLoaded, overheadCostLoaded)
        });
    }

    private static List<string> GetCacheRecommendations(bool m1Loaded, bool m2Loaded, bool m3Loaded)
    {
        var recommendations = new List<string>();

        if (!m1Loaded)
        {
            recommendations.Add("M1 (ManufactureCost) cache not loaded - check if department 'VYROBA' has cost data in accounting system");
            recommendations.Add("M1 (ManufactureCost) cache not loaded - verify products have ManufactureDifficulty (Complexity Points) configured");
            recommendations.Add("M1 (ManufactureCost) cache not loaded - check if products have ManufactureHistory records");
        }

        if (!m2Loaded)
        {
            recommendations.Add("M2 (SalesCost) cache not loaded - verify sales department cost data exists in accounting system");
        }

        if (!m3Loaded)
        {
            recommendations.Add("M3 (OverheadCost) cache not loaded - verify overhead cost data exists in accounting system");
        }

        if (m1Loaded && m2Loaded && m3Loaded)
        {
            recommendations.Add("All caches are loaded successfully - margin calculations will use cached data");
        }
        else if (!m1Loaded || !m2Loaded || !m3Loaded)
        {
            recommendations.Add("Some caches not loaded - affected margins (M0-M3) will show as 0 until periodic refresh succeeds");
            recommendations.Add("Check application logs for hydration task failures and periodic refresh attempts");
        }

        return recommendations;
    }
}