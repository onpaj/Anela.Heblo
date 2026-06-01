namespace Anela.Heblo.Domain.Features.Manufacture;

public class SubmitManufactureClientItem
{
    public string ProductCode { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string ProductName { get; set; } = string.Empty;
}