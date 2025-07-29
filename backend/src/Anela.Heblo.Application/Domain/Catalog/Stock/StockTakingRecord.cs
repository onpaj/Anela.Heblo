using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Application.Domain.Catalog.Stock;

public class StockTakingRecord : IEntity<int>
{
    public int Id { get; set; }
    
    public StockTakingType Type { get; set; }
    public string Code { get; set; }
    public double AmountNew { get; set; }
    public double AmountOld { get; set; }
    public DateTime Date { get; set; }
    public string? User { get; set; }
    public string? Error { get; set; }
}