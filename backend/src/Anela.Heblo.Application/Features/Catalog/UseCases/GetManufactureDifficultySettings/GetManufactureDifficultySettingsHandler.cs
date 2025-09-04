using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetManufactureDifficultySettings;

public class GetManufactureDifficultySettingsHandler : IRequestHandler<GetManufactureDifficultySettingsRequest, GetManufactureDifficultySettingsResponse>
{
    private readonly IManufactureDifficultyRepository _repository;
    private readonly IMapper _mapper;
    private readonly ILogger<GetManufactureDifficultySettingsHandler> _logger;
    private readonly TimeProvider _timeProvider;

    public GetManufactureDifficultySettingsHandler(
        IManufactureDifficultyRepository repository,
        IMapper mapper,
        ILogger<GetManufactureDifficultySettingsHandler> logger,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _mapper = mapper;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task<GetManufactureDifficultySettingsResponse> Handle(GetManufactureDifficultySettingsRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting manufacture difficulty history for product {ProductCode}", request.ProductCode);

        var settingsRecords = await _repository.ListAsync(request.ProductCode, request.AsOfDate, cancellationToken);

        var settingsDtos = settingsRecords.Select(h => _mapper.Map<ManufactureDifficultySettingDto>(h)).ToList();

        // Determine current setting
        var referenceDate = request.AsOfDate ?? _timeProvider.GetUtcNow();
        ManufactureDifficultySettingDto? currentSetting = null;

        foreach (var dto in settingsDtos)
        {
            var isCurrentForDate = (dto.ValidFrom == null || dto.ValidFrom <= referenceDate) &&
                                   (dto.ValidTo == null || dto.ValidTo >= referenceDate);
            dto.IsCurrent = isCurrentForDate;

            if (isCurrentForDate && currentSetting == null)
            {
                currentSetting = dto;
            }
        }

        var response = new GetManufactureDifficultySettingsResponse
        {
            ProductCode = request.ProductCode,
            Settings = settingsDtos,
            CurrentSetting = currentSetting
        };

        _logger.LogInformation("Retrieved {Count} difficulty history records for product {ProductCode}, current: {HasCurrent}",
            settingsDtos.Count, request.ProductCode, currentSetting != null);

        return response;
    }
}