using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.UpdateManufactureDifficulty;

public class UpdateManufactureDifficultyHandler : IRequestHandler<UpdateManufactureDifficultyRequest, UpdateManufactureDifficultyResponse>
{
    private readonly IManufactureDifficultyRepository _repository;
    private readonly ICatalogRepository _catalogRepository;
    private readonly IMapper _mapper;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<UpdateManufactureDifficultyHandler> _logger;

    public UpdateManufactureDifficultyHandler(
        IManufactureDifficultyRepository repository,
        ICatalogRepository catalogRepository,
        IMapper mapper,
        TimeProvider timeProvider,
        ILogger<UpdateManufactureDifficultyHandler> logger)
    {
        _repository = repository;
        _catalogRepository = catalogRepository;
        _mapper = mapper;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<UpdateManufactureDifficultyResponse> Handle(UpdateManufactureDifficultyRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Updating manufacture difficulty {Id} with value {DifficultyValue}",
            request.Id, request.DifficultyValue);

        var existing = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (existing == null)
        {
            throw new ArgumentException($"ManufactureDifficultyHistory with ID {request.Id} not found");
        }

        // Validate date range
        if (request.ValidFrom.HasValue && request.ValidTo.HasValue && request.ValidFrom >= request.ValidTo)
        {
            throw new ArgumentException("ValidFrom must be earlier than ValidTo");
        }

        // Check for overlaps (excluding the current record)
        var hasOverlap = await _repository.HasOverlapAsync(
            existing.ProductCode,
            request.ValidFrom,
            request.ValidTo,
            excludeId: request.Id,
            cancellationToken: cancellationToken);

        if (hasOverlap)
        {
            throw new InvalidOperationException($"The specified date range overlaps with existing difficulty settings for product {existing.ProductCode}");
        }

        // Update the entity using AutoMapper to ensure proper DateTime handling
        _mapper.Map(request, existing);

        var updated = await _repository.UpdateAsync(existing, cancellationToken);
        var dto = _mapper.Map<ManufactureDifficultySettingDto>(updated);

        // Refresh the catalog cache to update the CatalogAggregate with new difficulty settings
        await _catalogRepository.RefreshManufactureDifficultySettingsData(existing.ProductCode, cancellationToken);

        _logger.LogInformation("Updated manufacture difficulty {Id} for product {ProductCode} and refreshed catalog cache",
            updated.Id, updated.ProductCode);

        return new UpdateManufactureDifficultyResponse
        {
            DifficultyHistory = dto
        };
    }
}