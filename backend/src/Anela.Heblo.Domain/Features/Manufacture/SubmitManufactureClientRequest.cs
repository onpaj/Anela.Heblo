namespace Anela.Heblo.Domain.Features.Manufacture;

public class SubmitManufactureClientRequest
{
    public string ManufactureOrderCode { get; set; } = null!;
    public string ManufactureInternalNumber { get; set; } = null!;
    public DateTime Date { get; set; }
    public string? CreatedBy { get; set; }
    public List<SubmitManufactureClientItem> Items { get; set; } = [];
    public ManufactureType ManufactureType { get; set; }

    public string? LotNumber { get; set; }
    public DateOnly? ExpirationDate { get; set; }
}