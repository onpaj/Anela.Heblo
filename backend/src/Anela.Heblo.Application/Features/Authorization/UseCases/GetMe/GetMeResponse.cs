using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.GetMe;

public class GetMeResponse : BaseResponse
{
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
    public bool IsSuperUser { get; set; }
    public List<string> Permissions { get; set; } = new();
    public List<string> Groups { get; set; } = new();
}
