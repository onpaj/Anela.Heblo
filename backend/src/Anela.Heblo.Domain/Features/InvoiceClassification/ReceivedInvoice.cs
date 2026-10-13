namespace Anela.Heblo.Domain.Features.InvoiceClassification;

public class ReceivedInvoice
{
    public string AbraInvoiceId { get; set; } = string.Empty;

    public string InvoiceNumber { get; set; } = string.Empty;

    public string CompanyName { get; set; } = string.Empty;

    public string CompanyVat { get; set; } = string.Empty;

    /// <summary>Supplier DIČ (Flexi `dic`). Distinct from <see cref="CompanyVat"/>, which holds the IČ.</summary>
    public string? SupplierVatId { get; set; }

    public DateTime? InvoiceDate { get; set; }

    /// <summary>Flexi `datUcto` — the accounting date the cost belongs to.</summary>
    public DateTime? AccountingDate { get; set; }

    public decimal TotalAmount { get; set; }

    /// <summary>Flexi `sumZklCelkem` — total without VAT in document currency.</summary>
    public decimal TotalAmountWithoutVat { get; set; }

    /// <summary>Flexi `storno`.</summary>
    public bool IsCancelled { get; set; }

    public string Description { get; set; } = string.Empty;

    public List<ReceivedInvoiceItem> Items { get; set; } = new();

    public DateTime? DueDate { get; set; }

    public string? AccountingTemplateCode { get; set; }

    public string? DepartmentCode { get; set; }

    public string[] Labels { get; set; } = Array.Empty<string>();
}
