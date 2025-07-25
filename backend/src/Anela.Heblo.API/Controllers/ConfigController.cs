using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ConfigController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public ConfigController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// Provides runtime configuration for the React frontend
    /// This allows Azure AD secrets to be provided at runtime rather than build time
    /// </summary>
    [HttpGet("client")]
    public IActionResult GetClientConfig()
    {
        // For production environments, mock auth should be disabled by default
        var environment = _configuration["ASPNETCORE_ENVIRONMENT"] ?? "Development";
        var isProduction = environment.Equals("Production", StringComparison.OrdinalIgnoreCase);
        
        // Use backend configuration for mock auth decision, with environment-based defaults
        var useMockAuth = _configuration.GetValue<bool>("UseMockAuth", !isProduction);
        
        var config = new
        {
            ApiUrl = _configuration["REACT_APP_API_URL"] ?? Request.Scheme + "://" + Request.Host,
            UseMockAuth = useMockAuth,
            AzureClientId = _configuration["REACT_APP_AZURE_CLIENT_ID"] ?? "",
            AzureAuthority = _configuration["REACT_APP_AZURE_AUTHORITY"] ?? "",
            Environment = environment
        };

        return Ok(config);
    }
}