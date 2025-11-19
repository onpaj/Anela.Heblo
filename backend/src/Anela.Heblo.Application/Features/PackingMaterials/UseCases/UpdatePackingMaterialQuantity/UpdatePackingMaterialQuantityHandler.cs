using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.UpdatePackingMaterialQuantity;

public class UpdatePackingMaterialQuantityHandler : IRequestHandler<UpdatePackingMaterialQuantityRequest, UpdatePackingMaterialQuantityResponse>
{
    private readonly IPackingMaterialRepository _repository;
    private readonly ICurrentUserService _currentUserService;

    public UpdatePackingMaterialQuantityHandler(
        IPackingMaterialRepository repository,
        ICurrentUserService currentUserService)
    {
        _repository = repository;
        _currentUserService = currentUserService;
    }

    public async Task<UpdatePackingMaterialQuantityResponse> Handle(
        UpdatePackingMaterialQuantityRequest request,
        CancellationToken cancellationToken)
    {
        var material = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (material == null)
        {
            throw new ArgumentException($"PackingMaterial with ID {request.Id} not found");
        }

        var currentUser = _currentUserService.GetCurrentUser();
        material.UpdateQuantity(request.NewQuantity, request.Date, LogEntryType.Manual, currentUser?.Id);

        await _repository.UpdateAsync(material, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        // Recalculate forecast with updated data
        var oneMonthAgo = DateTime.UtcNow.AddMonths(-1);
        var recentLogs = await _repository.GetRecentLogsAsync(material.Id, oneMonthAgo, cancellationToken);
        var forecastedDays = material.CalculateForecastedDays(recentLogs.ToList());
        var displayForecast = forecastedDays == decimal.MaxValue ? null : (decimal?)Math.Round(forecastedDays, 1);

        var materialDto = new PackingMaterialDto
        {
            Id = material.Id,
            Name = material.Name,
            ConsumptionRate = material.ConsumptionRate,
            ConsumptionType = material.ConsumptionType,
            ConsumptionTypeText = GetConsumptionTypeText(material.ConsumptionType),
            CurrentQuantity = material.CurrentQuantity,
            ForecastedDays = displayForecast,
            CreatedAt = material.CreatedAt,
            UpdatedAt = material.UpdatedAt
        };

        return new UpdatePackingMaterialQuantityResponse
        {
            Material = materialDto
        };
    }

    private static string GetConsumptionTypeText(ConsumptionType type) => type switch
    {
        ConsumptionType.PerOrder => "za zakázku",
        ConsumptionType.PerProduct => "za produkt",
        ConsumptionType.PerDay => "za den",
        _ => type.ToString()
    };
}