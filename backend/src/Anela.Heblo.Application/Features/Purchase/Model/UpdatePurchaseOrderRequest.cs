using MediatR;
using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.Purchase.Model;

public class UpdatePurchaseOrderRequest : IRequest<UpdatePurchaseOrderResponse>
{
    [Required(ErrorMessage = "Purchase order ID is required")]
    public int Id { get; set; }

    [Required(ErrorMessage = "Supplier name is required")]
    [StringLength(200, ErrorMessage = "Supplier name cannot exceed 200 characters")]
    public string SupplierName { get; set; } = null!;

    public DateTime? ExpectedDeliveryDate { get; set; }

    [StringLength(1000, ErrorMessage = "Notes cannot exceed 1000 characters")]
    public string? Notes { get; set; }

    [Required(ErrorMessage = "Order lines are required")]
    public List<UpdatePurchaseOrderLineRequest> Lines { get; set; } = null!;

    [StringLength(50, ErrorMessage = "Order number cannot exceed 50 characters")]
    public string? OrderNumber { get; set; } // Optional custom order number
}

public class UpdatePurchaseOrderLineRequest
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Material ID is required")]
    [StringLength(50, ErrorMessage = "Material ID cannot exceed 50 characters")]
    public string MaterialId { get; set; } = null!; // Product code from catalog

    [StringLength(200, ErrorMessage = "Name cannot exceed 200 characters")]
    public string? Name { get; set; } // Optional - will use ProductName from catalog if available

    [Required(ErrorMessage = "Quantity is required")]
    [Range(0.01, 999999.99, ErrorMessage = "Quantity must be between 0.01 and 999999.99")]
    public decimal Quantity { get; set; }

    [Required(ErrorMessage = "Unit price is required")]
    [Range(0.00, 999999.99, ErrorMessage = "Unit price must be between 0.00 and 999999.99")]
    public decimal UnitPrice { get; set; }

    [StringLength(500, ErrorMessage = "Notes cannot exceed 500 characters")]
    public string? Notes { get; set; }
}