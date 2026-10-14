using Anela.Heblo.Application.Features.Packaging.Contracts;
using Anela.Heblo.Domain.Features.Packaging;
using MediatR;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.GetCarrierLabelOffsets;

/// <summary>
/// Returns one row per label-printing carrier; a carrier never saved yet is reported as 0 / 0.
/// </summary>
public class GetCarrierLabelOffsetsHandler : IRequestHandler<GetCarrierLabelOffsetsRequest, GetCarrierLabelOffsetsResponse>
{
    private readonly ICarrierLabelOffsetRepository _repository;

    public GetCarrierLabelOffsetsHandler(ICarrierLabelOffsetRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<GetCarrierLabelOffsetsResponse> Handle(
        GetCarrierLabelOffsetsRequest request,
        CancellationToken cancellationToken)
    {
        var stored = (await _repository.GetAllAsync(cancellationToken)).ToDictionary(o => o.Carrier);

        return new GetCarrierLabelOffsetsResponse
        {
            Offsets = CarrierLabelOffset.LabelCarriers
                .Select(carrier => stored.TryGetValue(carrier, out var offset)
                    ? new CarrierLabelOffsetDto
                    {
                        Carrier = carrier,
                        OffsetXMm = offset.OffsetXMm,
                        OffsetYMm = offset.OffsetYMm,
                        ModifiedAt = offset.ModifiedAt,
                        ModifiedBy = offset.ModifiedBy,
                    }
                    : new CarrierLabelOffsetDto { Carrier = carrier })
                .ToList(),
        };
    }
}
