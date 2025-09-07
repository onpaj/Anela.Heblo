using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Users;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.CreateManufactureDifficulty;

public class CreateManufactureDifficultyHandler : IRequestHandler<CreateManufactureDifficultyRequest, CreateManufactureDifficultyResponse>
{
    private readonly IManufactureDifficultyRepository _repository;
    private readonly ICatalogRepository _catalogRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IMapper _mapper;
    private readonly ILogger<CreateManufactureDifficultyHandler> _logger;

    public CreateManufactureDifficultyHandler(
        IManufactureDifficultyRepository repository,
        ICatalogRepository catalogRepository,
        ICurrentUserService currentUserService,
        IMapper mapper,
        ILogger<CreateManufactureDifficultyHandler> logger)
    {
        _repository = repository;
        _catalogRepository = catalogRepository;
        _currentUserService = currentUserService;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<CreateManufactureDifficultyResponse> Handle(CreateManufactureDifficultyRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating manufacture difficulty for product {ProductCode} with value {DifficultyValue}",
            request.ProductCode, request.DifficultyValue);

        // Validate date range
        if (request.ValidFrom.HasValue && request.ValidTo.HasValue && request.ValidFrom >= request.ValidTo)
        {
            return new CreateManufactureDifficultyResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.InvalidValue,
                Params = new Dictionary<string, string> { { "field", "ValidFrom must be earlier than ValidTo" } }
            };
        }

        // Resolve overlaps by adjusting existing periods
        await ResolveOverlapsAsync(request.ProductCode, request.ValidFrom, request.ValidTo, cancellationToken);

        var difficultyHistory = _mapper.Map<ManufactureDifficultySetting>(request);

        // Get current user information
        var currentUser = _currentUserService.GetCurrentUser();
        difficultyHistory.CreatedBy = currentUser.IsAuthenticated && !string.IsNullOrEmpty(currentUser.Name)
            ? currentUser.Name
            : "System";

        var created = await _repository.CreateAsync(difficultyHistory, cancellationToken);
        var dto = _mapper.Map<ManufactureDifficultySettingDto>(created);

        // Refresh the catalog cache to update the CatalogAggregate with new difficulty settings
        await _catalogRepository.RefreshManufactureDifficultySettingsData(request.ProductCode, cancellationToken);

        _logger.LogInformation("Created manufacture difficulty {Id} for product {ProductCode} and refreshed catalog cache",
            created.Id, created.ProductCode);

        return new CreateManufactureDifficultyResponse
        {
            DifficultyHistory = dto
        };
    }

    private async Task ResolveOverlapsAsync(string productCode, DateTime? newValidFrom, DateTime? newValidTo, CancellationToken cancellationToken)
    {
        // Get all existing settings for the product
        var existingSettings = await _repository.ListAsync(productCode, cancellationToken: cancellationToken);

        var overlappingSettings = existingSettings.Where(setting =>
            DoPeriodsOverlap(setting.ValidFrom, setting.ValidTo, newValidFrom, newValidTo))
            .ToList();

        if (!overlappingSettings.Any())
        {
            _logger.LogDebug("No overlapping periods found for product {ProductCode}", productCode);
            return;
        }

        _logger.LogInformation("Found {Count} overlapping periods for product {ProductCode}, resolving conflicts",
            overlappingSettings.Count, productCode);

        foreach (var overlappingSetting in overlappingSettings)
        {
            await ResolveOverlapAsync(overlappingSetting, newValidFrom, newValidTo, cancellationToken);
        }
    }

    private async Task ResolveOverlapAsync(ManufactureDifficultySetting existingSetting, DateTime? newValidFrom, DateTime? newValidTo, CancellationToken cancellationToken)
    {
        var originalValidFrom = existingSetting.ValidFrom;
        var originalValidTo = existingSetting.ValidTo;

        // Case 1: New period completely covers existing period - remove existing
        if (IsCompletelyOverlapped(existingSetting.ValidFrom, existingSetting.ValidTo, newValidFrom, newValidTo))
        {
            _logger.LogInformation("Removing completely overlapped period {Id} ({ValidFrom} - {ValidTo})",
                existingSetting.Id, originalValidFrom, originalValidTo);

            await _repository.DeleteAsync(existingSetting.Id, cancellationToken);
            return;
        }

        // Case 2: Existing period extends before new period - adjust ValidTo
        if (ShouldAdjustValidTo(existingSetting.ValidFrom, existingSetting.ValidTo, newValidFrom))
        {
            var newValidToForExisting = newValidFrom?.AddDays(-1);

            _logger.LogInformation("Adjusting ValidTo of period {Id} from {OldValidTo} to {NewValidTo}",
                existingSetting.Id, originalValidTo, newValidToForExisting);

            existingSetting.ValidTo = newValidToForExisting;
            await _repository.UpdateAsync(existingSetting, cancellationToken);
            return; // Don't check further conditions after adjusting ValidTo
        }

        // Case 3: Existing period extends after new period - adjust ValidFrom
        if (ShouldAdjustValidFrom(existingSetting.ValidFrom, existingSetting.ValidTo, newValidTo))
        {
            var newValidFromForExisting = newValidTo?.AddDays(1);

            _logger.LogInformation("Adjusting ValidFrom of period {Id} from {OldValidFrom} to {NewValidFrom}",
                existingSetting.Id, originalValidFrom, newValidFromForExisting);

            existingSetting.ValidFrom = newValidFromForExisting;
            await _repository.UpdateAsync(existingSetting, cancellationToken);
        }
    }

    private static bool DoPeriodsOverlap(DateTime? existingFrom, DateTime? existingTo, DateTime? newFrom, DateTime? newTo)
    {
        // Two periods overlap if:
        // 1. New period starts before existing ends (or existing has no end)
        // 2. New period ends after existing starts (or existing has no start)
        return (newFrom == null || existingTo == null || newFrom <= existingTo) &&
               (newTo == null || existingFrom == null || newTo >= existingFrom);
    }

    private static bool IsCompletelyOverlapped(DateTime? existingFrom, DateTime? existingTo, DateTime? newFrom, DateTime? newTo)
    {
        // Existing period is completely overlapped if:
        // - New period starts before or at the same time as existing
        // - New period ends after or at the same time as existing (or has no end when existing has end)
        var newStartsBeforeOrAt = newFrom == null || existingFrom == null || newFrom <= existingFrom;
        var newEndsAfterOrAt = newTo == null || existingTo == null || newTo >= existingTo;

        return newStartsBeforeOrAt && newEndsAfterOrAt;
    }

    private static bool ShouldAdjustValidTo(DateTime? existingFrom, DateTime? existingTo, DateTime? newFrom)
    {
        // Adjust ValidTo if existing period starts before new period and extends into it
        // Only when the new period has a definite start date (not null)
        if (newFrom == null) return false; // Can't adjust ValidTo when new period has no start

        return (existingFrom == null || existingFrom < newFrom) &&
               (existingTo == null || existingTo >= newFrom);
    }

    private static bool ShouldAdjustValidFrom(DateTime? existingFrom, DateTime? existingTo, DateTime? newTo)
    {
        // Adjust ValidFrom if existing period ends after new period and starts within it
        // Only when the new period has a definite end date (not null)
        if (newTo == null) return false; // Can't adjust ValidFrom when new period has no end

        return (existingTo == null || existingTo > newTo) &&
               (existingFrom == null || existingFrom <= newTo);
    }
}