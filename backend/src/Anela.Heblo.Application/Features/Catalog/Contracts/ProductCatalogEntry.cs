namespace Anela.Heblo.Application.Features.Catalog.Contracts;

public class ProductCatalogEntry
{
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string? Url { get; set; }
}
