using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetPackingMaterialsList;

public class GetPackingMaterialsListHandler : IRequestHandler<GetPackingMaterialsListRequest, GetPackingMaterialsListResponse>
{
    private readonly IPackingMaterialRepository _repository;
    private readonly ILogger<GetPackingMaterialsListHandler> _logger;

    public GetPackingMaterialsListHandler(
        IPackingMaterialRepository repository,
        ILogger<GetPackingMaterialsListHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<GetPackingMaterialsListResponse> Handle(
        GetPackingMaterialsListRequest request,
        CancellationToken cancellationToken)
    {
        var materials = (await _repository.GetAllAsync(cancellationToken)).ToList();
        var oneMonthAgo = DateTime.UtcNow.AddMonths(-1);

        var logsByMaterial = await _repository.GetRecentLogsForMaterialsAsync(
            materials.Select(m => m.Id),
            oneMonthAgo,
            cancellationToken);

        var withForecast = 0;
        var withoutForecast = 0;
        var totalLogs = 0;

        var materialDtos = materials.Select(material =>
        {
            var recentLogs = logsByMaterial.TryGetValue(material.Id, out var logs)
                ? logs.ToList()
                : new List<PackingMaterialLog>();
            totalLogs += recentLogs.Count;

            var forecastedDays = material.CalculateForecastedDays(recentLogs);
            var displayForecast = forecastedDays == decimal.MaxValue
                ? null
                : (decimal?)Math.Round(forecastedDays, 1);

            if (displayForecast.HasValue) withForecast++;
            else withoutForecast++;

            return new PackingMaterialDto
            {
                Id = material.Id,
                Name = material.Name,
                ConsumptionRate = material.ConsumptionRate,
                ConsumptionType = material.ConsumptionType,
                ConsumptionTypeText = PackingMaterialsTextHelper.ConsumptionTypeText(material.ConsumptionType),
                CurrentQuantity = material.CurrentQuantity,
                ForecastedDays = displayForecast,
                CreatedAt = material.CreatedAt,
                UpdatedAt = material.UpdatedAt
            };
        }).ToList();

        _logger.LogDebug(
            "PackingMaterials list: materials={Count}, logsLoaded={LogCount}, withForecast={WithForecast}, withoutForecast={WithoutForecast}",
            materialDtos.Count, totalLogs, withForecast, withoutForecast);

        return new GetPackingMaterialsListResponse
        {
            Materials = materialDtos
        };
    }
}