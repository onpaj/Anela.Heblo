using Anela.Heblo.Domain.Features.Purchase;

namespace Anela.Heblo.Application.Features.Purchase.Contracts;

public class PurchaseOrderLineDto
{
    public int Id { get; set; }
    public string MaterialId { get; set; } = null!;
    public string Code { get; set; } = null!; // Same as MaterialId for compatibility
    public string MaterialName { get; set; } = null!;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    public string? Notes { get; set; }
    public string? CatalogNote { get; set; } // Note from catalog item

    public static PurchaseOrderLineDto FromLine(PurchaseOrderLine line, string? catalogNote = null) =>
        new()
        {
            Id = line.Id,
            MaterialId = line.MaterialId,
            Code = line.MaterialId,
            MaterialName = line.MaterialName,
            Quantity = line.Quantity,
            UnitPrice = line.UnitPrice,
            LineTotal = line.LineTotal,
            Notes = line.Notes,
            CatalogNote = catalogNote
        };
}