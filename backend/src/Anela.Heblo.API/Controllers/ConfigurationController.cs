using Microsoft.AspNetCore.Mvc;
using System.Reflection;

namespace Anela.Heblo.API.Controllers;

[ApiController]
[Route("[controller]")]
public class ConfigurationController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ConfigurationController> _logger;

    public ConfigurationController(IConfiguration configuration, ILogger<ConfigurationController> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult GetConfiguration()
    {
        try
        {
            // Priority order for version:
            // 1. APP_VERSION (set by CI/CD pipeline with GitVersion)
            // 2. Assembly informational version
            // 3. Assembly version
            // 4. Fallback to 1.0.0
            var version = Environment.GetEnvironmentVariable("APP_VERSION");
            
            if (string.IsNullOrEmpty(version))
            {
                var assembly = Assembly.GetExecutingAssembly();
                version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                         ?? assembly.GetName().Version?.ToString()
                         ?? "1.0.0";
            }

            var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
            var useMockAuth = _configuration.GetValue<bool>("UseMockAuth", false);

            var config = new
            {
                Version = version,
                Environment = environment,
                UseMockAuth = useMockAuth,
                Timestamp = DateTime.UtcNow
            };

            _logger.LogDebug("Configuration requested: {@Config}", config);

            return Ok(config);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving application configuration");
            return StatusCode(500, new { Error = "Failed to retrieve configuration" });
        }
    }
}