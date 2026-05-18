using MediatR;

namespace Anela.Heblo.Application.Features.WeatherForecast.UseCases.GetWeatherForecast;

public sealed record GetWeatherForecastRequest : IRequest<GetWeatherForecastResponse>;
