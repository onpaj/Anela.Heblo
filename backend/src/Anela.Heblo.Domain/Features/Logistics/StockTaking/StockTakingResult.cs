using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.Logistics.StockTaking;

public class StockTakingResult : Entity<int>
{
    public StockTakingType Type { get; set; }
    public string Code { get; set; }
    public double AmountNew { get; set; }
    public double AmountOld { get; set; }
    public DateTime Date { get; set; }
    public string? User { get; set; }
    public string? Error { get; set; }
}