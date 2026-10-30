namespace Anela.Heblo.Domain.Features.Catalog.Attributes;

/// <summary>Writes the manual "top product" flag to the ERP, where it is stored as a product attribute.</summary>
public interface ICatalogTopProductWriter
{
    /// <summary>
    /// Marks the product as a top product, or clears the mark. Unmarking removes the attribute
    /// instead of storing false, so "not set" and "false" both read back as not top.
    /// </summary>
    /// <exception cref="InvalidOperationException">The ERP rejected the write.</exception>
    Task SetAsync(string productCode, bool isTopProduct, CancellationToken ct);
}
