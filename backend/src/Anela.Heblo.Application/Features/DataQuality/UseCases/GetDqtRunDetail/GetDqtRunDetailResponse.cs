using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.DataQuality.UseCases.GetDqtRunDetail;

public class GetDqtRunDetailResponse : BaseResponse
{
    public DqtRunDto? Run { get; set; }
    public List<InvoiceDqtResultDto> Results { get; set; } = new();
}
