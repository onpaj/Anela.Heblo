using MediatR;
using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.Purchase.Model;

public class UpdatePurchaseOrderRequest : IRequest<UpdatePurchaseOrderResponse>
{
    [Required]
    public int Id { get; set; }
    
    [Required]
    public string SupplierName { get; set; } = null!;
    
    public DateTime? ExpectedDeliveryDate { get; set; }
    
    public string? Notes { get; set; }
    
    [Required]
    public List<UpdatePurchaseOrderLineRequest> Lines { get; set; } = null!;
    
    public string? OrderNumber { get; set; } // Optional custom order number
}

public class UpdatePurchaseOrderLineRequest
{
    public int? Id { get; set; }
    
    [Required]
    public string MaterialId { get; set; } = null!; // Product code from catalog
    
    public string? Name { get; set; } // Optional - will use ProductName from catalog if available
    
    [Required]
    public decimal Quantity { get; set; }
    
    [Required]
    public decimal UnitPrice { get; set; }
    
    public string? Notes { get; set; }
}