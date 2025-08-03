using MediatR;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.Purchase.Model;

public class CreatePurchaseOrderRequest : IRequest<CreatePurchaseOrderResponse>
{
    [Required]
    public string SupplierName { get; set; } = null!;
    
    [Required]
    public string OrderDate { get; set; } = null!;
    
    public string? ExpectedDeliveryDate { get; set; }
    
    public string? Notes { get; set; }
    
    public string? OrderNumber { get; set; } // Optional custom order number
    
    public List<CreatePurchaseOrderLineRequest>? Lines { get; set; }
}

public class CreatePurchaseOrderLineRequest
{
    [Required]
    public string MaterialId { get; set; } = null!;
    
    public string? Name { get; set; } // Optional - will use ProductName from catalog if available
    
    [Required]
    public decimal Quantity { get; set; }
    
    [Required]
    public decimal UnitPrice { get; set; }
    
    public string? Notes { get; set; }
}