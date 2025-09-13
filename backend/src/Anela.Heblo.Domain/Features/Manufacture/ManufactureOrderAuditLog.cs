namespace Anela.Heblo.Domain.Features.Manufacture;

public class ManufactureOrderAuditLog
{
    public int Id { get; set; }
    public int ManufactureOrderId { get; set; }
    public DateTime Timestamp { get; set; }
    public string User { get; set; } = null!; // User display name
    public ManufactureOrderAuditAction Action { get; set; }
    public string Details { get; set; } = null!; // JSON s detaily změny
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }

    // Navigation property
    public ManufactureOrder ManufactureOrder { get; set; } = null!;
}