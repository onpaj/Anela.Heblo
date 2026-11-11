using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Domain.Features.PackingMaterials;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetDailyConsumptionBreakdown;

public class GetDailyConsumptionBreakdownHandler
    : IRequestHandler<GetDailyConsumptionBreakdownRequest, GetDailyConsumptionBreakdownResponse>
{
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
        _logger.LogInformation("Loading daily consumption breakdown for {Date} grouped by {GroupBy}", request.Date, request.GroupBy);

        var consumptions = (await _repository.GetConsumptionsByDateAsync(request.Date, cancellationToken)).ToList();

        if (consumptions.Count == 0)
            return new GetDailyConsumptionBreakdownResponse { Success = true, Date = request.Date, GroupBy = request.GroupBy.ToString() };

        var consumedMaterialIds = consumptions.Select(c => c.PackingMaterialId).Distinct().ToList();
        var materialNameById = await _repository.GetMaterialNamesByIdsAsync(consumedMaterialIds, cancellationToken);

        var groups = request.GroupBy switch
        {
            ConsumptionGroupBy.Material => BuildGroupByMaterial(consumptions, materialNameById),
            ConsumptionGroupBy.Product => BuildGroupByProduct(consumptions, materialNameById),
            ConsumptionGroupBy.Order => BuildGroupByOrder(consumptions, materialNameById),
            _ => throw new ArgumentOutOfRangeException(nameof(request.GroupBy), request.GroupBy, "Unhandled GroupBy value.")
        };

        return new GetDailyConsumptionBreakdownResponse
        {
            Success = true,
            Date = request.Date,
            GroupBy = request.GroupBy.ToString(),
            Groups = groups
        };
    }

    private static List<ConsumptionGroupDto> BuildGroupByMaterial(
        List<PackingMaterialConsumption> consumptions,
        IReadOnlyDictionary<int, string> materialNameById)
    {
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
        IReadOnlyDictionary<int, string> materialNameById)
    {
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
        IReadOnlyDictionary<int, string> materialNameById)
    {
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
