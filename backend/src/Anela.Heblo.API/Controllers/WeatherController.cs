using Anela.Heblo.Application.Features.Weather.Model;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WeatherController : ControllerBase
{
    private readonly IMediator _mediator;

    public WeatherController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("forecast")]
    public async Task<IEnumerable<GetWeatherForecastResponse>> GetWeatherForecast(CancellationToken cancellationToken)
    {
        var request = new GetWeatherForecastRequest();
        return await _mediator.Send(request, cancellationToken);
    }
}