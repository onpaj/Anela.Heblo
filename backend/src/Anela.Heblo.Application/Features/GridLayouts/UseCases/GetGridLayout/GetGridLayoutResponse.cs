using Anela.Heblo.Application.Features.GridLayouts.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.GridLayouts.UseCases.GetGridLayout;

public class GetGridLayoutResponse : BaseResponse
{
    public GridLayoutDto? Layout { get; set; }
}
