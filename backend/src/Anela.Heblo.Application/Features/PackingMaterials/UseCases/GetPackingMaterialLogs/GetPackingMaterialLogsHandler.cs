using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using MediatR;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetPackingMaterialLogs;

public class GetPackingMaterialLogsHandler : IRequestHandler<GetPackingMaterialLogsRequest, GetPackingMaterialLogsResponse>
{
    private readonly IPackingMaterialRepository _repository;

    public GetPackingMaterialLogsHandler(IPackingMaterialRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetPackingMaterialLogsResponse> Handle(
        GetPackingMaterialLogsRequest request,
        CancellationToken cancellationToken)
    {
        var material = await _repository.GetByIdWithLogsAsync(request.PackingMaterialId, cancellationToken);
        if (material == null)
        {
            throw new InvalidOperationException($"Packing material with ID {request.PackingMaterialId} not found.");
        }

        var fromDate = DateTime.UtcNow.AddDays(-request.Days);
        var recentLogs = await _repository.GetRecentLogsAsync(request.PackingMaterialId, fromDate, cancellationToken);

        var materialDto = new PackingMaterialDto
        {
            Id = material.Id,
            Name = material.Name,
            ConsumptionRate = material.ConsumptionRate,
            ConsumptionType = material.ConsumptionType,
            ConsumptionTypeText = GetConsumptionTypeText(material.ConsumptionType),
            CurrentQuantity = material.CurrentQuantity,
            CreatedAt = material.CreatedAt,
            UpdatedAt = material.UpdatedAt
        };

        var logDtos = recentLogs.Select(log => new PackingMaterialLogDto
        {
            Id = log.Id,
            PackingMaterialId = log.PackingMaterialId,
            Date = log.Date,
            OldQuantity = log.OldQuantity,
            NewQuantity = log.NewQuantity,
            ChangeAmount = log.ChangeAmount,
            LogType = log.LogType,
            LogTypeText = GetLogTypeText(log.LogType),
            UserId = log.UserId,
            CreatedAt = log.CreatedAt
        }).OrderByDescending(log => log.Date).ThenByDescending(log => log.CreatedAt).ToList();

        return new GetPackingMaterialLogsResponse
        {
            Material = materialDto,
            Logs = logDtos
        };
    }

    private static string GetConsumptionTypeText(ConsumptionType type) => type switch
    {
        ConsumptionType.PerOrder => "za zakázku",
        ConsumptionType.PerProduct => "za produkt",
        ConsumptionType.PerDay => "za den",
        _ => type.ToString()
    };

    private static string GetLogTypeText(LogEntryType type) => type switch
    {
        LogEntryType.Manual => "Ruční",
        LogEntryType.AutomaticConsumption => "Automatická spotřeba",
        _ => type.ToString()
    };
}