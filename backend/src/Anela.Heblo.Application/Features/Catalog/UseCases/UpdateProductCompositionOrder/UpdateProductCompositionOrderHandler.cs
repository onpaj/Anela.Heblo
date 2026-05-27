using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.UpdateProductCompositionOrder;

public class UpdateProductCompositionOrderHandler
    : IRequestHandler<UpdateProductCompositionOrderRequest, UpdateProductCompositionOrderResponse>
{
    private readonly IProductIngredientOrderRepository _repository;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<UpdateProductCompositionOrderHandler> _logger;

    public UpdateProductCompositionOrderHandler(
        IProductIngredientOrderRepository repository,
        ICurrentUserService currentUserService,
        ILogger<UpdateProductCompositionOrderHandler> logger)
    {
        _repository = repository;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<UpdateProductCompositionOrderResponse> Handle(
        UpdateProductCompositionOrderRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await _repository.ListByParentAsync(request.ProductCode, cancellationToken);
        var existingByCode = existing.ToDictionary(x => x.IngredientProductCode);
        var requestedCodes = request.Order
            .Select(x => x.IngredientProductCode)
            .ToHashSet();

        var user = _currentUserService.GetCurrentUser();
        var updatedBy = user.IsAuthenticated && !string.IsNullOrEmpty(user.Name)
            ? user.Name
            : "System";
        var now = DateTime.UtcNow;

        // Delete obsolete rows
        foreach (var row in existing.Where(x => !requestedCodes.Contains(x.IngredientProductCode)))
        {
            _logger.LogInformation(
                "Deleting obsolete ingredient order row {Id} for {Parent}/{Ingredient}",
                row.Id, request.ProductCode, row.IngredientProductCode);
            await _repository.DeleteAsync(row.Id, cancellationToken);
        }

        // Upsert requested rows
        var changes = 0;
        foreach (var item in request.Order)
        {
            if (existingByCode.TryGetValue(item.IngredientProductCode, out var current))
            {
                current.SortOrder = item.SortOrder;
                current.UpdatedAt = now;
                current.UpdatedBy = updatedBy;
                await _repository.UpdateAsync(current, cancellationToken);
            }
            else
            {
                await _repository.CreateAsync(new ProductIngredientOrder
                {
                    ParentProductCode = request.ProductCode,
                    IngredientProductCode = item.IngredientProductCode,
                    SortOrder = item.SortOrder,
                    UpdatedAt = now,
                    UpdatedBy = updatedBy
                }, cancellationToken);
            }
            changes++;
        }

        return new UpdateProductCompositionOrderResponse
        {
            UpdatedCount = changes
        };
    }
}
