namespace Anela.Heblo.Domain.Features.Manufacture;

public class ManufactureErpDocumentItem
{
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public double Amount { get; set; }
    public string? Unit { get; set; }
    public string? LotNumber { get; set; }
    public DateOnly? ExpirationDate { get; set; }
}
