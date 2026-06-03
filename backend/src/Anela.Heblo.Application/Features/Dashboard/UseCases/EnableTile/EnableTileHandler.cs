using Anela.Heblo.Application.Features.Dashboard.Infrastructure;
using Anela.Heblo.Application.Features.Dashboard.UseCases.GetUserSettings;
using Anela.Heblo.Domain.Features.Dashboard;
using MediatR;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.EnableTile;

public class EnableTileHandler : IRequestHandler<EnableTileRequest, EnableTileResponse>
{
    private readonly IUserDashboardSettingsRepository _repository;
    private readonly IUserDashboardSettingsLock _lock;
    private readonly TimeProvider _timeProvider;
    private readonly IMediator _mediator;

    public EnableTileHandler(
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

    public async Task<EnableTileResponse> Handle(EnableTileRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.TileId))
        {
            return new EnableTileResponse(Anela.Heblo.Application.Shared.ErrorCodes.RequiredFieldMissing);
        }

        var userId = string.IsNullOrEmpty(request.UserId) ? "anonymous" : request.UserId;

        // Trigger provisioning outside the write lock (lock is non-reentrant)
        await _mediator.Send(new GetUserSettingsRequest { UserId = userId }, cancellationToken);

        await using var lockHandle = await _lock.AcquireAsync(userId, cancellationToken);

        var settings = await _repository.GetByUserIdAsync(userId);
        if (settings == null)
        {
            return new EnableTileResponse();
        }

        var existingTile = settings.Tiles.FirstOrDefault(t => t.TileId == request.TileId);
        if (existingTile != null)
        {
            existingTile.IsVisible = true;
            existingTile.LastModified = _timeProvider.GetUtcNow().DateTime;
        }
        else
        {
            var maxOrder = settings.Tiles.Any() ? settings.Tiles.Max(t => t.DisplayOrder) : -1;
            settings.Tiles.Add(new UserDashboardTile
            {
                UserId = userId,
                TileId = request.TileId,
                IsVisible = true,
                DisplayOrder = maxOrder + 1,
                LastModified = _timeProvider.GetUtcNow().DateTime,
                DashboardSettings = settings
            });
        }

        settings.LastModified = _timeProvider.GetUtcNow().DateTime;
        await _repository.UpdateAsync(settings);

        return new EnableTileResponse();
    }
}
