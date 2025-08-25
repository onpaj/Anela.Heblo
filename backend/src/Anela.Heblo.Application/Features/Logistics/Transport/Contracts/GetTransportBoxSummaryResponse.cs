namespace Anela.Heblo.Application.Features.Logistics.Transport.Contracts;

public class GetTransportBoxSummaryResponse
{
    public int TotalBoxes { get; set; }
    public int ActiveBoxes { get; set; } // All states except Closed
    public Dictionary<string, int> StatesCounts { get; set; } = new();
}