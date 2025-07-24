using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web.Resource;
using Microsoft.Graph;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[ApiController]
[Route("[controller]")]
public class WeatherForecastController : ControllerBase
{
    private static readonly string[] Summaries = new[]
    {
        "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
    };

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
            // Real mode - get user from Graph API
            var user = await _graphServiceClient.Me.GetAsync();
            userName = user?.DisplayName ?? User.Identity?.Name ?? "Unknown User";
        }

        _logger.LogInformation("Weather forecast requested by user: {UserName}", userName);

        return Enumerable.Range(1, 5).Select(index => new WeatherForecast
            {
                Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                TemperatureC = Random.Shared.Next(-20, 55),
                Summary = Summaries[Random.Shared.Next(Summaries.Length)]
            })
            .ToArray();
    }
}