using Anela.Heblo.Application.Features.Weather.Contracts;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Weather.Application;

public class GetWeatherForecastUseCase
{
    private readonly IWeatherService _weatherService;
    private readonly ILogger<GetWeatherForecastUseCase> _logger;

    public GetWeatherForecastUseCase(IWeatherService weatherService, ILogger<GetWeatherForecastUseCase> logger)
    {
        _weatherService = weatherService;
        _logger = logger;
    }

    public async Task<IEnumerable<GetWeatherForecastResponse>> ExecuteAsync(GetWeatherForecastRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Executing GetWeatherForecast use case");
        
        return await _weatherService.GetForecastAsync(cancellationToken);
    }
}