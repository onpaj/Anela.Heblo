using Anela.Heblo.Application.Features.Dashboard.Infrastructure;
using Anela.Heblo.Application.Features.Dashboard.UseCases.GetUserSettings;
using Anela.Heblo.Domain.Features.Dashboard;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.SaveUserSettings;

public class SaveUserSettingsHandler : IRequestHandler<SaveUserSettingsRequest, SaveUserSettingsResponse>
{
    private readonly IUserDashboardSettingsRepository _repository;
    private readonly IUserDashboardSettingsLock _lock;
    private readonly TimeProvider _timeProvider;
    private readonly IMediator _mediator;
    private readonly ICurrentUserService _currentUserService;

    public SaveUserSettingsHandler(
        IUserDashboardSettingsRepository repository,
        IUserDashboardSettingsLock @lock,
        TimeProvider timeProvider,
        IMediator mediator,
        ICurrentUserService currentUserService)
    {
        _repository = repository;
        _lock = @lock;
        _timeProvider = timeProvider;
        _mediator = mediator;
        _currentUserService = currentUserService;
    }

    public async Task<SaveUserSettingsResponse> Handle(SaveUserSettingsRequest request, CancellationToken cancellationToken)
    {
        var userId = string.IsNullOrEmpty(_currentUserService.GetCurrentUser().Id) ? "anonymous" : _currentUserService.GetCurrentUser().Id;

        // Trigger provisioning outside the write lock (lock is non-reentrant)
        await _mediator.Send(new GetUserSettingsRequest(), cancellationToken);

        await using var lockHandle = await _lock.AcquireAsync(userId, cancellationToken);

        var settings = await _repository.GetByUserIdAsync(userId);
        if (settings == null)
        {
            return new SaveUserSettingsResponse();
        }

        // Update tile settings
        if (request.Tiles != null)
        {
            foreach (var tileDto in request.Tiles)
            {
                var existingTile = settings.Tiles.FirstOrDefault(t => t.TileId == tileDto.TileId);
                if (existingTile != null)
                {
                    existingTile.IsVisible = tileDto.IsVisible;
                    existingTile.DisplayOrder = tileDto.DisplayOrder;
                    existingTile.LastModified = _timeProvider.GetUtcNow().DateTime;
                }
                else
                {
                    settings.Tiles.Add(new UserDashboardTile
                    {
                        UserId = userId,
                        TileId = tileDto.TileId,
                        IsVisible = tileDto.IsVisible,
                        DisplayOrder = tileDto.DisplayOrder,
                        LastModified = _timeProvider.GetUtcNow().DateTime,
                        DashboardSettings = settings
                    });
                }
            }
        }

        settings.UserId = userId;
        settings.LastModified = _timeProvider.GetUtcNow().DateTime;
        await _repository.UpdateAsync(settings);

        return new SaveUserSettingsResponse();
    }
}
