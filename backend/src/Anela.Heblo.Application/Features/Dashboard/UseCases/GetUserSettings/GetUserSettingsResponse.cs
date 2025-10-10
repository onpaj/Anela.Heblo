using Anela.Heblo.Application.Features.Dashboard.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.GetUserSettings;

public class GetUserSettingsResponse : BaseResponse
{
    public UserDashboardSettingsDto Settings { get; set; } = new();
}