using MediatR;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.GetUserSettings;

public class GetUserSettingsRequest : IRequest<GetUserSettingsResponse>
{
    public string UserId { get; set; } = string.Empty;
}