using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web.Resource;
using Microsoft.Graph;
using Anela.Heblo.Domain.Constants;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[ApiController]
[Route("[controller]")]
public class WeatherForecastController : ControllerBase
{

    private readonly ILogger<WeatherForecastController> _logger;
    private readonly GraphServiceClient? _graphServiceClient;
    private readonly IConfiguration _configuration;

    public WeatherForecastController(ILogger<WeatherForecastController> logger,
        IConfiguration configuration,
        GraphServiceClient? graphServiceClient = null)
    {
        _logger = logger;
        _configuration = configuration;
        _graphServiceClient = graphServiceClient;
    }

    [HttpGet(Name = "GetWeatherForecast")]
    public async Task<IEnumerable<WeatherForecast>> Get()
    {
        string? userName = "Unknown User";

        if (_configuration.GetValue<bool>("UseMockAuth"))
        {
            // Mock mode - get user from claims
            userName = User.Identity?.Name ?? "Mock User";
        }
        else if (_graphServiceClient != null)
        {
            try
            {
                // Real mode - get user from Graph API
                var user = await _graphServiceClient.Me.GetAsync();
                userName = user?.DisplayName ?? User.Identity?.Name ?? "Unknown User";
            }
            catch (Exception ex)
            {
                // If Graph API fails (e.g., consent required), fallback to JWT claims
                _logger.LogWarning("Failed to get user from Graph API: {Error}. Using fallback.", ex.Message);
                userName = User.FindFirst("name")?.Value ??
                          User.FindFirst("preferred_username")?.Value ??
                          User.Identity?.Name ??
                          "Authenticated User";
            }
        }

        _logger.LogInformation("Weather forecast requested by user: {UserName}", userName);

        return Enumerable.Range(1, WeatherConstants.FORECAST_DAYS).Select(index => new WeatherForecast
        {
            Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            TemperatureC = Random.Shared.Next(WeatherConstants.MIN_TEMPERATURE, WeatherConstants.MAX_TEMPERATURE),
            Summary = WeatherConstants.WEATHER_SUMMARIES[Random.Shared.Next(WeatherConstants.WEATHER_SUMMARIES.Length)]
        })
            .ToArray();
    }
}