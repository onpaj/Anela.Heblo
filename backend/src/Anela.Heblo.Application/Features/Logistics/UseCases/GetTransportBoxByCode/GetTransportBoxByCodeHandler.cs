using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Features.Logistics.Contracts.Models;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GetTransportBoxByCode;

public class GetTransportBoxByCodeHandler : IRequestHandler<GetTransportBoxByCodeRequest, GetTransportBoxByCodeResponse>
{
    private readonly ILogger<GetTransportBoxByCodeHandler> _logger;
    private readonly ITransportBoxRepository _repository;
    private readonly ILogisticsCatalogSource _catalogSource;
    private readonly IMapper _mapper;

    public GetTransportBoxByCodeHandler(
        ILogger<GetTransportBoxByCodeHandler> logger,
        ITransportBoxRepository repository,
        ILogisticsCatalogSource catalogSource,
        IMapper mapper)
    {
        _logger = logger;
        _repository = repository;
        _catalogSource = catalogSource;
        _mapper = mapper;
    }

    public async Task<GetTransportBoxByCodeResponse> Handle(GetTransportBoxByCodeRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting transport box with code: {BoxCode}", request.BoxCode);

        if (string.IsNullOrWhiteSpace(request.BoxCode))
        {
            _logger.LogWarning("Empty box code provided");
            return new GetTransportBoxByCodeResponse(ErrorCodes.RequiredFieldMissing,
                new Dictionary<string, string> { { "Field", "BoxCode" } });
        }

        var normalizedBoxCode = request.BoxCode.Trim().ToUpper();
        var transportBox = await _repository.GetByCodeAsync(normalizedBoxCode);

        if (transportBox == null)
        {
            _logger.LogWarning("Transport box with code {BoxCode} not found", request.BoxCode);
            return new GetTransportBoxByCodeResponse(ErrorCodes.TransportBoxNotFound,
                new Dictionary<string, string> { { "BoxCode", request.BoxCode } });
        }

        // Check if box is in a receivable state (InTransit, Reserve, or Quarantine)
        var isReceivable = transportBox.State == TransportBoxState.Reserve
            || transportBox.State == TransportBoxState.InTransit
            || transportBox.State == TransportBoxState.Quarantine;
        if (!isReceivable)
        {
            _logger.LogInformation("Transport box {BoxCode} is in state {State}, not receivable but will load details",
                request.BoxCode, transportBox.State);
        }

        // Load full details including items
        var detailedBox = await _repository.GetByIdWithDetailsAsync(transportBox.Id);
        if (detailedBox == null)
        {
            _logger.LogError("Failed to load detailed data for transport box {BoxCode}", request.BoxCode);
            return new GetTransportBoxByCodeResponse(ErrorCodes.DatabaseError,
                new Dictionary<string, string> { { "BoxCode", request.BoxCode } });
        }

        var dto = _mapper.Map<TransportBoxDto>(detailedBox);
        dto.IsReceivable = isReceivable;

        // Enrich each item with catalog image and current stock
        var codes = dto.Items.Select(i => i.ProductCode).Distinct().ToList();
        var catalogByCode = new Dictionary<string, LogisticsCatalogItem>(StringComparer.Ordinal);
        foreach (var code in codes)
        {
            var item = await _catalogSource.GetCatalogItemAsync(code, cancellationToken);
            if (item != null)
                catalogByCode[code] = item;
        }

        foreach (var itemDto in dto.Items)
        {
            if (catalogByCode.TryGetValue(itemDto.ProductCode, out var catalogItem))
            {
                itemDto.ImageUrl = catalogItem.Image;
                itemDto.OnStock = catalogItem.EshopStock;
            }
            else
            {
                _logger.LogWarning(
                    "Catalog item not found for product code {ProductCode} in transport box {BoxCode}; leaving image/stock unset",
                    itemDto.ProductCode, dto.Code);
            }
        }

        _logger.LogInformation("Retrieved transport box {BoxCode} with {ItemCount} items in {State} state",
            detailedBox.Code, detailedBox.Items.Count, detailedBox.State);

        return new GetTransportBoxByCodeResponse { TransportBox = dto };
    }
}
