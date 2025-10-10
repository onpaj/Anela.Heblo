using Anela.Heblo.Application.Features.Dashboard.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.SaveUserSettings;

public class SaveUserSettingsRequest : IRequest<SaveUserSettingsResponse>
{
    public string UserId { get; set; } = string.Empty;
    public UserDashboardTileDto[] Tiles { get; set; } = [];
}