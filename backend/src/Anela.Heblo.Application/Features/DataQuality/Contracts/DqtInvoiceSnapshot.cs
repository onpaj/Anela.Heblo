namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

public class DqtInvoiceSourceQuery
{
    public string RequestId { get; set; } = string.Empty;
    public DateOnly DateFrom { get; set; }
    public DateOnly DateTo { get; set; }
}

public class DqtInvoiceSnapshot
{
    public string Code { get; set; } = string.Empty;
    public decimal TotalWithVat { get; set; }
    public decimal TotalWithoutVat { get; set; }
    public List<DqtInvoiceItem> Items { get; set; } = new();
}

public class DqtInvoiceItem
{
    public string Code { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal WithVat { get; set; }
    public decimal WithoutVat { get; set; }
}
