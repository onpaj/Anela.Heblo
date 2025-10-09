using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GetTransportBoxSummary;

public class GetTransportBoxSummaryResponse : BaseResponse
{
    public int TotalBoxes { get; set; }
    public int ActiveBoxes { get; set; } // All states except Closed
    public Dictionary<string, int> StatesCounts { get; set; } = new();
}