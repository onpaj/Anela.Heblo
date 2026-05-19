using Anela.Heblo.Application.Features.WeatherForecast.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.WeatherForecast.UseCases.GetWeatherForecast;

public class GetWeatherForecastResponse : BaseResponse
{
    // Empty list on both error and "no data" paths. The frontend hook throws on !data.success,
    // so Days is only consumed when Success is true and the list is populated.
    public List<HottestDayDto> Days { get; set; } = new();

    public GetWeatherForecastResponse() { }

    public GetWeatherForecastResponse(ErrorCodes errorCode)
        : base(errorCode)
    { }
}
