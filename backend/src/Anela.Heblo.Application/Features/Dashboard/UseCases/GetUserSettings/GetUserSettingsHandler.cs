using Anela.Heblo.Application.Features.Dashboard.Infrastructure;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.GetUserSettings;

public class GetUserSettingsHandler : IRequestHandler<GetUserSettingsRequest, GetUserSettingsResponse>
{
    private readonly IUserDashboardSettingsReader _reader;
    private readonly ICurrentUserService _currentUserService;

    public GetUserSettingsHandler(
        IUserDashboardSettingsReader reader,
        ICurrentUserService currentUserService)
    {
        _reader = reader;
        _currentUserService = currentUserService;
    }

    public async Task<GetUserSettingsResponse> Handle(GetUserSettingsRequest request, CancellationToken cancellationToken)
    {
        var currentUserId = _currentUserService.GetCurrentUser().Id;
        var userId = string.IsNullOrEmpty(currentUserId) ? "anonymous" : currentUserId;

        var settings = await _reader.GetEffectiveSettingsAsync(userId, cancellationToken);

        return new GetUserSettingsResponse { Settings = settings };
    }
}
