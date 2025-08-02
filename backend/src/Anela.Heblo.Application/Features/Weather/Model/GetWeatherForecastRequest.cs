using MediatR;

namespace Anela.Heblo.Application.Features.Weather.Model;

public record GetWeatherForecastRequest : IRequest<IEnumerable<GetWeatherForecastResponse>>
{
    // Empty request for now - could add parameters like days, location etc.
}