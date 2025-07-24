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
        var config = new
        {
            ApiUrl = _configuration["REACT_APP_API_URL"] ?? Request.Scheme + "://" + Request.Host,
            UseMockAuth = bool.Parse(_configuration["REACT_APP_USE_MOCK_AUTH"] ?? "true"),
            AzureClientId = _configuration["REACT_APP_AZURE_CLIENT_ID"] ?? "",
            AzureAuthority = _configuration["REACT_APP_AZURE_AUTHORITY"] ?? ""
        };

        return Ok(config);
    }
}