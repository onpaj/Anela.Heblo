namespace Anela.Heblo.Application.Features.Manufacture.Contracts;

public class ManufacturedProductInventoryItemDto
{
    public int Id { get; set; }
    public string ProductCode { get; set; } = null!;
    public string ProductName { get; set; } = null!;
    public string? LotNumber { get; set; }
    public DateOnly? ExpirationDate { get; set; }
    public decimal Amount { get; set; }
    public int? ManufactureOrderId { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = null!;
    public DateTime? LastModifiedAt { get; set; }
    public string? LastModifiedBy { get; set; }
    public List<ManufacturedProductInventoryLogDto> Log { get; set; } = new();
}
