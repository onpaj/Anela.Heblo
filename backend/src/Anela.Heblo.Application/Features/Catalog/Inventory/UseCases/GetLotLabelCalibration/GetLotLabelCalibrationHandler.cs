using Anela.Heblo.Domain.Features.Catalog.Inventory;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.GetLotLabelCalibration;

public class GetLotLabelCalibrationHandler
    : IRequestHandler<GetLotLabelCalibrationRequest, GetLotLabelCalibrationResponse>
{
    private readonly ILotLabelCalibrationRepository _repository;

    public GetLotLabelCalibrationHandler(ILotLabelCalibrationRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetLotLabelCalibrationResponse> Handle(
        GetLotLabelCalibrationRequest request, CancellationToken cancellationToken)
    {
        var calibration = await _repository.GetAsync(cancellationToken);

        return new GetLotLabelCalibrationResponse
        {
            PitchDots = calibration.PitchDots,
            MinPitchDots = LotLabelCalibration.MinPitchDots,
            MaxPitchDots = LotLabelCalibration.MaxPitchDots,
            DriftDotsPer10Labels = calibration.DriftDotsPer10Labels,
            MinDriftDotsPer10Labels = LotLabelCalibration.MinDriftDotsPer10Labels,
            MaxDriftDotsPer10Labels = LotLabelCalibration.MaxDriftDotsPer10Labels,
        };
    }
}
