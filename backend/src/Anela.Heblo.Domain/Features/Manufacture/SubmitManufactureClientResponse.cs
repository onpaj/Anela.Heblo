namespace Anela.Heblo.Domain.Features.Manufacture;

public class SubmitManufactureClientResponse
{
    public string ManufactureId { get; set; } = null!;
    public string? MaterialIssueForSemiProductDocCode { get; set; }
    public string? SemiProductReceiptDocCode { get; set; }
    public string? SemiProductIssueForProductDocCode { get; set; }
    public string? MaterialIssueForProductDocCode { get; set; }
    public string? ProductReceiptDocCode { get; set; }
}
