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

    // ABRA Flexi stock-document codes captured from SubmitManufactureAsync results.
    // 1. Material issue for semi-product (V-VYDEJ-MATERIAL, phase A)
    public string? DocMaterialIssueForSemiProduct { get; set; }
    public DateTime? DocMaterialIssueForSemiProductDate { get; set; }

    // 2. Semi-product receipt (V-PRIJEM-POLOTOVAR, phase A)
    public string? DocSemiProductReceipt { get; set; }
    public DateTime? DocSemiProductReceiptDate { get; set; }

    // 3. Semi-product issue for product (V-VYDEJ-POLOTOVAR, phase B)
    public string? DocSemiProductIssueForProduct { get; set; }
    public DateTime? DocSemiProductIssueForProductDate { get; set; }

    // 4. Material issue for product (V-VYDEJ-MATERIAL, phase B, optional)
    public string? DocMaterialIssueForProduct { get; set; }
    public DateTime? DocMaterialIssueForProductDate { get; set; }

    // 5. Product receipt (V-PRIJEM-VYROBEK, phase B)
    public string? DocProductReceipt { get; set; }
    public DateTime? DocProductReceiptDate { get; set; }

    public bool? WeightWithinTolerance { get; set; }
    public decimal? WeightDifference { get; set; }   // positive = surplus, negative = deficit

}