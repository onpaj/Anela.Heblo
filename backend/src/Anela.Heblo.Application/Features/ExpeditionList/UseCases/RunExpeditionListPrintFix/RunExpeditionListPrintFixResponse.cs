using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.ExpeditionList.UseCases.RunExpeditionListPrintFix;

public class RunExpeditionListPrintFixResponse : BaseResponse
{
    public int TotalCount { get; set; }
    public string? ErrorMessage { get; set; }
}
