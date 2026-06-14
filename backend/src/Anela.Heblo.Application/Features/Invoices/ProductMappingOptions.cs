using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.Invoices;

public class ProductMappingOptions
{
    public const string SectionName = "ProductMapping";

    [Required]
    public string ShoptetCode { get; set; } = string.Empty;

    [Required]
    public string ErpCode { get; set; } = string.Empty;
}
