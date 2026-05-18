using Anela.Heblo.Application.Features.WeatherForecast.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.WeatherForecast.UseCases.GetWeatherForecast;

public class GetWeatherForecastResponse : BaseResponse
{
    public List<HottestDayDto> Days { get; set; } = new();

    public GetWeatherForecastResponse() { }

    public GetWeatherForecastResponse(ErrorCodes errorCode)
        : base(errorCode)
    { }
}
