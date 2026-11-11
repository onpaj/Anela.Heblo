namespace Anela.Heblo.Domain.Features.Catalog.Eshop;

public class EshopProductFlag
{
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
}

public class EshopProduct
{
    /// <summary>Variant code.</summary>
    public string Code { get; set; } = "";
    public string ProductGuid { get; set; } = "";
    /// <summary>Product name.</summary>
    public string Name { get; set; } = "";
    public string? VariantName { get; set; }
    /// <summary>Product visibility == "visible" and variant visible.</summary>
    public bool IsVisible { get; set; }
    /// <summary>Variant image, falling back to the product main image.</summary>
    public string? ImageUrl { get; set; }
    /// <summary>Product main image.</summary>
    public string? DefaultImageUrl { get; set; }
    /// <summary>Weight in grams (API kilograms x 1000); zero maps to null.</summary>
    public double? WeightGrams { get; set; }
    public double? Height { get; set; }
    public double? Width { get; set; }
    public double? Depth { get; set; }
    public bool AtypicalShipping { get; set; }
    public IReadOnlyList<EshopProductFlag> Flags { get; set; } = Array.Empty<EshopProductFlag>();
}
