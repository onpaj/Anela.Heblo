namespace Anela.Heblo.Application.Features.Catalog.Model;

public class GetMaterialsForPurchaseResponse
{
    public List<MaterialForPurchaseDto> Materials { get; set; } = new();
}

public class MaterialForPurchaseDto
{
    public required string ProductCode { get; set; }
    public required string ProductName { get; set; }
    public required string ProductType { get; set; }
    public decimal? LastPurchasePrice { get; set; }
    public string? Location { get; set; }
    public int CurrentStock { get; set; }
    public string? MinimalOrderQuantity { get; set; }
}