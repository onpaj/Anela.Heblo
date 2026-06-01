using Anela.Heblo.Application.Features.Dashboard.Infrastructure;
using Anela.Heblo.Application.Features.Dashboard.UseCases.GetUserSettings;
using Anela.Heblo.Xcc.Services.Dashboard;
using MediatR;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.DisableTile;

public class DisableTileHandler : IRequestHandler<DisableTileRequest, DisableTileResponse>
{
    private readonly IUserDashboardSettingsRepository _repository;
    private readonly IUserDashboardSettingsLock _lock;
    private readonly TimeProvider _timeProvider;
    private readonly IMediator _mediator;

    public DisableTileHandler(
        IUserDashboardSettingsRepository repository,
        IUserDashboardSettingsLock @lock,
        TimeProvider timeProvider,
        IMediator mediator)
    {
        _repository = repository;
        _lock = @lock;
        _timeProvider = timeProvider;
        _mediator = mediator;
    }

    public async Task<DisableTileResponse> Handle(DisableTileRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.TileId))
        {
            return new DisableTileResponse(Anela.Heblo.Application.Shared.ErrorCodes.RequiredFieldMissing);
        }

        var userId = string.IsNullOrEmpty(request.UserId) ? "anonymous" : request.UserId;

        // Trigger provisioning outside the write lock (lock is non-reentrant)
        await _mediator.Send(new GetUserSettingsRequest { UserId = userId }, cancellationToken);

        await using var lockHandle = await _lock.AcquireAsync(userId, cancellationToken);

        var settings = await _repository.GetByUserIdAsync(userId);
        if (settings == null)
        {
            return new DisableTileResponse();
        }

        var existingTile = settings.Tiles.FirstOrDefault(t => t.TileId == request.TileId);
        if (existingTile != null)
        {
            existingTile.IsVisible = false;
            existingTile.LastModified = _timeProvider.GetUtcNow().DateTime;
            settings.LastModified = _timeProvider.GetUtcNow().DateTime;

            await _repository.UpdateAsync(settings);
        }

        return new DisableTileResponse();
    }
}
