namespace Anela.Heblo.Domain.Features.Manufacture;

public class ManufactureOrderNote
{
    public int Id { get; set; }
    public int ManufactureOrderId { get; set; }
    public string Text { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public string CreatedByUser { get; set; } = null!;

    // Navigation property
    public ManufactureOrder ManufactureOrder { get; set; } = null!;
}