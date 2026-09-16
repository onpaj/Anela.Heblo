using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.NudgeLotLabelCalibration;

public class NudgeLotLabelCalibrationHandler
    : IRequestHandler<NudgeLotLabelCalibrationRequest, NudgeLotLabelCalibrationResponse>
{
    private readonly ILotLabelCalibrationRepository _repository;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<NudgeLotLabelCalibrationHandler> _logger;

    public NudgeLotLabelCalibrationHandler(
        ILotLabelCalibrationRepository repository,
        ICurrentUserService currentUserService,
        ILogger<NudgeLotLabelCalibrationHandler> logger)
    {
        _repository = repository;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<NudgeLotLabelCalibrationResponse> Handle(
        NudgeLotLabelCalibrationRequest request, CancellationToken cancellationToken)
    {
        var currentUser = _currentUserService.GetCurrentUser();
        if (string.IsNullOrEmpty(currentUser.Id))
        {
            return new NudgeLotLabelCalibrationResponse(ErrorCodes.Unauthorized);
        }

        // Read-modify-write on a single row with no concurrency token: two operators nudging
        // within the same few milliseconds would have the second write silently replace the
        // first. Accepted as is. There is one printer, the window is the span of this
        // handler, and a lost nudge is self-correcting: the drift is still visible on the
        // next batch and the operator clicks once more. (A token would be unverifiable in
        // the InMemory-backed tests, the same trade-off the project made elsewhere.)
        var current = await _repository.GetAsync(cancellationToken);
        var corrected = current.Nudge(request.Direction, request.Speed, currentUser.Id);

        if (corrected.EffectivePitchHundredths == current.EffectivePitchHundredths)
        {
            _logger.LogWarning(
                "Lot label calibration nudge {Direction} at {Speed} speed by {User} was ignored: " +
                "already at the limit with pitch {PitchDots} drift {DriftDotsPer100Labels}",
                request.Direction, request.Speed, currentUser.Id,
                current.PitchDots, current.DriftDotsPer100Labels);

            return new NudgeLotLabelCalibrationResponse { IsAtLimit = true };
        }

        // Captured before saving: this pair is the audit trail for a change an operator can
        // now make without the label-calibration permission.
        var previousPitchDots = current.PitchDots;
        var previousDriftDotsPer100Labels = current.DriftDotsPer100Labels;

        await _repository.SaveAsync(corrected, cancellationToken);

        _logger.LogInformation(
            "Lot label calibration nudged {Direction} at {Speed} speed by {User}: " +
            "pitch {OldPitch} drift {OldDrift} -> pitch {NewPitch} drift {NewDrift}",
            request.Direction, request.Speed, currentUser.Id,
            previousPitchDots, previousDriftDotsPer100Labels,
            corrected.PitchDots, corrected.DriftDotsPer100Labels);

        return new NudgeLotLabelCalibrationResponse { IsAtLimit = false };
    }
}
