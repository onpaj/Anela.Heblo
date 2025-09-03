using Anela.Heblo.Application.Features.Catalog.Model;
using Anela.Heblo.Domain.Features.Catalog;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Handlers;

public class DeleteManufactureDifficultyHandler : IRequestHandler<DeleteManufactureDifficultyRequest, DeleteManufactureDifficultyResponse>
{
    private readonly IManufactureDifficultyRepository _repository;
    private readonly ICatalogRepository _catalogRepository;
    private readonly ILogger<DeleteManufactureDifficultyHandler> _logger;

    public DeleteManufactureDifficultyHandler(
        IManufactureDifficultyRepository repository,
        ICatalogRepository catalogRepository,
        ILogger<DeleteManufactureDifficultyHandler> logger)
    {
        _repository = repository;
        _catalogRepository = catalogRepository;
        _logger = logger;
    }

    public async Task<DeleteManufactureDifficultyResponse> Handle(DeleteManufactureDifficultyRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Deleting manufacture difficulty {Id}", request.Id);

        try
        {
            var existing = await _repository.GetByIdAsync(request.Id, cancellationToken);
            if (existing == null)
            {
                return new DeleteManufactureDifficultyResponse
                {
                    Success = false,
                    Message = $"ManufactureDifficultyHistory with ID {request.Id} not found"
                };
            }

            await _repository.DeleteAsync(request.Id, cancellationToken);

            // Refresh the catalog cache to update the CatalogAggregate with updated difficulty settings
            await _catalogRepository.RefreshManufactureDifficultySettingsData(existing.ProductCode, cancellationToken);

            _logger.LogInformation("Deleted manufacture difficulty {Id} for product {ProductCode} and refreshed catalog cache", 
                request.Id, existing.ProductCode);

            return new DeleteManufactureDifficultyResponse
            {
                Success = true,
                Message = "Manufacture difficulty deleted successfully"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting manufacture difficulty {Id}", request.Id);
            
            return new DeleteManufactureDifficultyResponse
            {
                Success = false,
                Message = $"Error deleting manufacture difficulty: {ex.Message}"
            };
        }
    }
}