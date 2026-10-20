using Anela.Heblo.Application.Features.Dashboard.Contracts;
using Anela.Heblo.Application.Features.Dashboard.Infrastructure;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.SaveUserSettings;

internal sealed class SaveUserSettingsHandler : IRequestHandler<SaveUserSettingsRequest, SaveUserSettingsResponse>
{
    private readonly IUserDashboardSettingsMutator _mutator;
    private readonly ICurrentUserService _currentUserService;

    public SaveUserSettingsHandler(
        IUserDashboardSettingsMutator mutator,
        ICurrentUserService currentUserService)
    {
        _mutator = mutator;
        _currentUserService = currentUserService;
    }

    public async Task<SaveUserSettingsResponse> Handle(SaveUserSettingsRequest request, CancellationToken cancellationToken)
    {
        var currentUser = _currentUserService.GetCurrentUser();
        var userId = currentUser.Id;

        await _mutator.MutateBulkAsync(
            userId,
            request.Tiles ?? Array.Empty<UserDashboardTileDto>(),
            cancellationToken);

        return new SaveUserSettingsResponse();
    }
}
