namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

public class InvoiceDqtResultDto
{
    public Guid Id { get; set; }
    public string InvoiceCode { get; set; } = string.Empty;
    public int MismatchType { get; set; }
    public List<string> MismatchFlags { get; set; } = new();
    public string? ShoptetValue { get; set; }
    public string? FlexiValue { get; set; }
    public string? Details { get; set; }
}
