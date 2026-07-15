using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.SetLotLabelCalibration;

public class SetLotLabelCalibrationHandler
    : IRequestHandler<SetLotLabelCalibrationRequest, SetLotLabelCalibrationResponse>
{
    private readonly ILotLabelCalibrationRepository _repository;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<SetLotLabelCalibrationHandler> _logger;

    public SetLotLabelCalibrationHandler(
        ILotLabelCalibrationRepository repository,
        ICurrentUserService currentUserService,
        ILogger<SetLotLabelCalibrationHandler> logger)
    {
        _repository = repository;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<SetLotLabelCalibrationResponse> Handle(
        SetLotLabelCalibrationRequest request, CancellationToken cancellationToken)
    {
        var currentUser = _currentUserService.GetCurrentUser();
        if (string.IsNullOrEmpty(currentUser.Id))
        {
            return new SetLotLabelCalibrationResponse(ErrorCodes.Unauthorized);
        }

        var calibration = new LotLabelCalibration(request.PitchDots, request.DriftDotsPer100Labels, currentUser.Id);
        await _repository.SaveAsync(calibration, cancellationToken);

        _logger.LogInformation(
            "Lot label calibration set to pitch {PitchDots} dots, drift {DriftDotsPer100Labels} dots per 100 labels by {User}",
            request.PitchDots, request.DriftDotsPer100Labels, currentUser.Id);

        return new SetLotLabelCalibrationResponse
        {
            PitchDots = request.PitchDots,
            DriftDotsPer100Labels = request.DriftDotsPer100Labels,
        };
    }
}
