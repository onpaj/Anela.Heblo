using Anela.Heblo.Application.Features.WeatherForecast.UseCases.GetWeatherForecast;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[GateOn(Feature.Admin_Administration)]
[ApiController]
[Route("api/weather-forecast")]
public class WeatherForecastController : BaseApiController
{
    private readonly IMediator _mediator;

    public WeatherForecastController(IMediator mediator)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    [HttpGet]
    public async Task<ActionResult<GetWeatherForecastResponse>> Get(
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new GetWeatherForecastRequest(), cancellationToken);
        return HandleResponse(response);
    }
}
