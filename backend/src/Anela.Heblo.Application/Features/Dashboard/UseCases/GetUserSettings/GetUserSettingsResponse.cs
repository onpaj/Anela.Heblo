using Anela.Heblo.Application.Features.Dashboard.Contracts;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.GetUserSettings;

public class GetUserSettingsResponse
{
    public UserDashboardSettingsDto Settings { get; set; } = new();
}