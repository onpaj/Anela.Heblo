using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.RecalculateMargin;

public class RecalculateMarginResponse : BaseResponse
{
    public List<MarginHistoryDto> MarginHistory { get; set; } = new();
}
