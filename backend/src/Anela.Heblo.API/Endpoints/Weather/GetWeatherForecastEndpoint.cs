using Anela.Heblo.Application.Features.Weather.Application;
using Anela.Heblo.Application.Features.Weather.Contracts;
using FastEndpoints;

namespace Anela.Heblo.API.Endpoints.Weather;

public class GetWeatherForecastEndpoint : EndpointWithoutRequest<IEnumerable<GetWeatherForecastResponse>>
{
    private readonly GetWeatherForecastUseCase _useCase;

    public GetWeatherForecastEndpoint(GetWeatherForecastUseCase useCase)
    {
        _useCase = useCase;
    }

    public override void Configure()
    {
        Get("/api/weatherforecast");
        AllowAnonymous();
        Summary(s =>
        {
            s.Summary = "Get weather forecast";
            s.Description = "Returns a 5-day weather forecast";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var request = new GetWeatherForecastRequest();
        var result = await _useCase.ExecuteAsync(request, ct);
        Response = result;
    }
}