using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using MediatR;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetPackingMaterialsList;

public class GetPackingMaterialsListHandler : IRequestHandler<GetPackingMaterialsListRequest, GetPackingMaterialsListResponse>
{
    private readonly IPackingMaterialRepository _repository;

    public GetPackingMaterialsListHandler(IPackingMaterialRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetPackingMaterialsListResponse> Handle(
        GetPackingMaterialsListRequest request,
        CancellationToken cancellationToken)
    {
        var materials = await _repository.GetAllWithLogsAsync(cancellationToken);
        var oneMonthAgo = DateTime.UtcNow.AddMonths(-1);

        var materialDtos = materials.Select(material =>
        {
            var recentLogs = material.Logs
                .Where(log => log.CreatedAt >= oneMonthAgo)
                .ToList();

            var forecastedDays = material.CalculateForecastedDays(recentLogs);
            var displayForecast = forecastedDays == decimal.MaxValue ? null : (decimal?)Math.Round(forecastedDays, 1);

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

        return new GetPackingMaterialsListResponse
        {
            Materials = materialDtos
        };
    }
}