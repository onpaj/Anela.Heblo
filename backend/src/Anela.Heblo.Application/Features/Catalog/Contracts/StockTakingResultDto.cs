namespace Anela.Heblo.Application.Features.Catalog.Contracts;

public class StockTakingResultDto
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public double AmountNew { get; set; }
    public double AmountOld { get; set; }
    public DateTime Date { get; set; }
    public string User { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
}