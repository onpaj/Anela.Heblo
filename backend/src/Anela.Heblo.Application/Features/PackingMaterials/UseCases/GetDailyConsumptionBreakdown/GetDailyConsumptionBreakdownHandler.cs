using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Domain.Features.PackingMaterials;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetDailyConsumptionBreakdown;

public class GetDailyConsumptionBreakdownHandler
    : IRequestHandler<GetDailyConsumptionBreakdownRequest, GetDailyConsumptionBreakdownResponse>
{
    private static readonly HashSet<string> ValidGroupByValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "material", "product", "order"
    };

    private readonly IPackingMaterialRepository _repository;
    private readonly ILogger<GetDailyConsumptionBreakdownHandler> _logger;

    public GetDailyConsumptionBreakdownHandler(
        IPackingMaterialRepository repository,
        ILogger<GetDailyConsumptionBreakdownHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<GetDailyConsumptionBreakdownResponse> Handle(
        GetDailyConsumptionBreakdownRequest request,
        CancellationToken cancellationToken)
    {
        if (!ValidGroupByValues.Contains(request.GroupBy))
        {
            return new GetDailyConsumptionBreakdownResponse
            {
                Success = false,
                Error = $"Invalid GroupBy value '{request.GroupBy}'. Must be one of: material, product, order.",
                Date = request.Date,
                GroupBy = request.GroupBy
            };
        }

        try
        {
            _logger.LogInformation("Loading daily consumption breakdown for {Date} grouped by {GroupBy}", request.Date, request.GroupBy);

            var consumptions = (await _repository.GetConsumptionsByDateAsync(request.Date, cancellationToken)).ToList();

            if (consumptions.Count == 0)
                return new GetDailyConsumptionBreakdownResponse { Success = true, Date = request.Date, GroupBy = request.GroupBy };

            var materials = (await _repository.GetAllWithAllocationsAsync(cancellationToken)).ToList();

            var groups = request.GroupBy.ToLowerInvariant() switch
            {
                "material" => BuildGroupByMaterial(consumptions, materials),
                "product" => BuildGroupByProduct(consumptions, materials),
                "order" => BuildGroupByOrder(consumptions, materials),
                _ => throw new InvalidOperationException($"Unhandled GroupBy value: {request.GroupBy}")
            };

            return new GetDailyConsumptionBreakdownResponse
            {
                Success = true,
                Date = request.Date,
                GroupBy = request.GroupBy,
                Groups = groups
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading daily consumption breakdown for {Date}", request.Date);

            return new GetDailyConsumptionBreakdownResponse
            {
                Success = false,
                Error = "An unexpected error occurred while loading the breakdown.",
                Date = request.Date,
                GroupBy = request.GroupBy
            };
        }
    }

    private static List<ConsumptionGroupDto> BuildGroupByMaterial(
        List<PackingMaterialConsumption> consumptions,
        List<PackingMaterial> materials)
    {
        var materialNameById = materials.ToDictionary(m => m.Id, m => m.Name);

        return consumptions
            .GroupBy(c => c.PackingMaterialId)
            .Select(g =>
            {
                var materialName = materialNameById.TryGetValue(g.Key, out var name) ? name : "Unknown";
                var details = g
                    .Where(c => c.InvoiceId != null)
                    .GroupBy(c => c.InvoiceId!)
                    .Select(dg => new ConsumptionDetailDto
                    {
                        Key = dg.Key,
                        Label = dg.Key,
                        Amount = dg.Sum(c => c.Amount)
                    })
                    .ToList();

                return new ConsumptionGroupDto
                {
                    Key = g.Key.ToString(),
                    Label = materialName,
                    TotalAmount = g.Sum(c => c.Amount),
                    RowCount = g.Count(),
                    Details = details
                };
            })
            .OrderByDescending(g => g.TotalAmount)
            .ToList();
    }

    private static List<ConsumptionGroupDto> BuildGroupByProduct(
        List<PackingMaterialConsumption> consumptions,
        List<PackingMaterial> materials)
    {
        var materialNameById = materials.ToDictionary(m => m.Id, m => m.Name);

        return consumptions
            .Where(c => c.ProductCode != null)
            .GroupBy(c => c.ProductCode!)
            .Select(g =>
            {
                var details = g
                    .GroupBy(c => c.PackingMaterialId)
                    .Select(dg =>
                    {
                        var materialName = materialNameById.TryGetValue(dg.Key, out var name) ? name : "Unknown";
                        return new ConsumptionDetailDto
                        {
                            Key = dg.Key.ToString(),
                            Label = materialName,
                            Amount = dg.Sum(c => c.Amount)
                        };
                    })
                    .ToList();

                return new ConsumptionGroupDto
                {
                    Key = g.Key,
                    Label = g.Key,
                    TotalAmount = g.Sum(c => c.Amount),
                    RowCount = g.Count(),
                    Details = details
                };
            })
            .OrderByDescending(g => g.TotalAmount)
            .ToList();
    }

    private static List<ConsumptionGroupDto> BuildGroupByOrder(
        List<PackingMaterialConsumption> consumptions,
        List<PackingMaterial> materials)
    {
        var materialNameById = materials.ToDictionary(m => m.Id, m => m.Name);

        return consumptions
            .Where(c => c.InvoiceId != null)
            .GroupBy(c => c.InvoiceId!)
            .Select(g =>
            {
                var details = g
                    .GroupBy(c => c.PackingMaterialId)
                    .Select(dg =>
                    {
                        var materialName = materialNameById.TryGetValue(dg.Key, out var name) ? name : "Unknown";
                        return new ConsumptionDetailDto
                        {
                            Key = dg.Key.ToString(),
                            Label = materialName,
                            Amount = dg.Sum(c => c.Amount)
                        };
                    })
                    .ToList();

                return new ConsumptionGroupDto
                {
                    Key = g.Key,
                    Label = g.Key,
                    TotalAmount = g.Sum(c => c.Amount),
                    RowCount = g.Count(),
                    Details = details
                };
            })
            .OrderByDescending(g => g.TotalAmount)
            .ToList();
    }
}
