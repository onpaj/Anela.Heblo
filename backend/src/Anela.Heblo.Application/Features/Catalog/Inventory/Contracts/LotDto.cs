namespace Anela.Heblo.Application.Features.Catalog.Inventory.Contracts;

public class LotDto
{
    public int Id { get; set; }
    public string MaterialCode { get; set; } = null!;
    public string LotCode { get; set; } = null!;
    public DateOnly? Expiration { get; set; }
    public DateOnly ReceivedDate { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = null!;
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
