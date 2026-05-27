using Anela.Heblo.Domain.Features.Manufacture;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.UpdateProductCompositionOrder;

public class UpdateProductCompositionOrderHandler
    : IRequestHandler<UpdateProductCompositionOrderRequest, UpdateProductCompositionOrderResponse>
{
    private readonly IManufactureClient _manufactureClient;
    private readonly ILogger<UpdateProductCompositionOrderHandler> _logger;

    public UpdateProductCompositionOrderHandler(
        IManufactureClient manufactureClient,
        ILogger<UpdateProductCompositionOrderHandler> logger)
    {
        _manufactureClient = manufactureClient ?? throw new ArgumentNullException(nameof(manufactureClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<UpdateProductCompositionOrderResponse> Handle(
        UpdateProductCompositionOrderRequest request,
        CancellationToken cancellationToken)
    {
        var template = await _manufactureClient.GetManufactureTemplateAsync(request.ProductCode, cancellationToken);
        if (template is null)
        {
            _logger.LogWarning(
                "Cannot set BoM order for {ProductCode}: manufacture template not found in Flexi",
                request.ProductCode);
            return new UpdateProductCompositionOrderResponse { UpdatedCount = 0 };
        }

        var codeToBomItemId = template.Ingredients.ToDictionary(
            i => i.ProductCode,
            i => i.TemplateId,
            StringComparer.Ordinal);

        var tuples = new List<(int BoMItemId, int Order, string? PhaseLabel)>();
        foreach (var item in request.Order)
        {
            if (!codeToBomItemId.TryGetValue(item.IngredientProductCode, out var bomItemId))
            {
                _logger.LogWarning(
                    "Ingredient {IngredientCode} not found in Flexi BoM for {ProductCode} — skipping",
                    item.IngredientProductCode, request.ProductCode);
                continue;
            }
            tuples.Add((bomItemId, item.SortOrder, NormalizePhaseLabel(item.PhaseLabel)));
        }

        if (tuples.Count == 0)
        {
            return new UpdateProductCompositionOrderResponse { UpdatedCount = 0 };
        }

        await _manufactureClient.SetBomItemsOrderAndPhaseAsync(request.ProductCode, tuples, cancellationToken);

        return new UpdateProductCompositionOrderResponse { UpdatedCount = tuples.Count };
    }

    /// <summary>
    /// Returns a single uppercase letter A–Z if the input is valid, or null otherwise.
    /// Defends against non-letter or multi-character labels from the client.
    /// </summary>
    private static string? NormalizePhaseLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return null;

        var trimmed = label.Trim().ToUpperInvariant();
        return trimmed.Length == 1 && trimmed[0] is >= 'A' and <= 'Z' ? trimmed : null;
    }
}
