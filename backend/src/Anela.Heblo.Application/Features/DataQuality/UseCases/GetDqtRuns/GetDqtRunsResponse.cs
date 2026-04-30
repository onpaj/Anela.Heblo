using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.DataQuality.UseCases.GetDqtRuns;

public class GetDqtRunsResponse : BaseResponse
{
    public List<DqtRunDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;
}
