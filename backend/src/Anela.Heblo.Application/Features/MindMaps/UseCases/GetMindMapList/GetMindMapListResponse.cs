using Anela.Heblo.Application.Features.MindMaps.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.GetMindMapList;

public class GetMindMapListResponse : BaseResponse
{
    public List<MindMapListItemDto> Items { get; set; } = new();
}
