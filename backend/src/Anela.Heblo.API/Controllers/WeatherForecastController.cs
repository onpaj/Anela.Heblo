using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Anela.Heblo.Application.Interfaces;
using Anela.Heblo.Domain.Entities;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[ApiController]
[Route("[controller]")]
public class WeatherForecastController : ControllerBase
{
    private readonly ILogger<WeatherForecastController> _logger;
    private readonly IWeatherService _weatherService;
    private readonly IUserService _userService;

    public WeatherForecastController(
        ILogger<WeatherForecastController> logger,
        IWeatherService weatherService,
        IUserService userService)
    {
        _logger = logger;
        _weatherService = weatherService;
        _userService = userService;
    }

    [HttpGet(Name = "GetWeatherForecast")]
    public async Task<IEnumerable<WeatherForecast>> Get()
    {
        var userName = await _userService.GetCurrentUserNameAsync();
        _logger.LogInformation("Weather forecast requested by user: {UserName}", userName);

        return await _weatherService.GetForecastAsync();
    }
}