using Anela.Heblo.Application.Features.Authorization.UseCases;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.GetGroups;

public class GetGroupsResponse : BaseResponse
{
    public List<GroupSummaryDto> Groups { get; set; } = new();
}
