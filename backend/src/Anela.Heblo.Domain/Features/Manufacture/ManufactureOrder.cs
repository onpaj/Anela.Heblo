namespace Anela.Heblo.Domain.Features.Manufacture;

public class ManufactureOrder
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = null!; // Auto-generated: MO-YYYY-XXX
    public DateTime CreatedDate { get; set; }
    public string CreatedByUser { get; set; } = null!; // User display name
    public string? ResponsiblePerson { get; set; } // User display name

    // Manufacturing workflow type
    public ManufactureType ManufactureType { get; set; } = ManufactureType.MultiPhase;

    // Planning date (unified for both single-phase and multi-phase)
    public DateOnly PlannedDate { get; set; }

    // State management
    public ManufactureOrderState State { get; set; }
    public DateTime StateChangedAt { get; set; }
    public string StateChangedByUser { get; set; } = null!;

    // Collections
    public ManufactureOrderSemiProduct? SemiProduct { get; set; } = null!;
    public List<ManufactureOrderProduct> Products { get; set; } = new();
    public List<ManufactureOrderNote> Notes { get; set; } = new();
    public bool ManualActionRequired { get; set; } = false;
    public string? ErpOrderNumberSemiproduct { get; set; }
    public DateTime? ErpOrderNumberSemiproductDate { get; set; }
    public string? ErpOrderNumberProduct { get; set; }
    public DateTime? ErpOrderNumberProductDate { get; set; }
    public string? ErpDiscardResidueDocumentNumber { get; set; }
    public DateTime? ErpDiscardResidueDocumentNumberDate { get; set; }

}