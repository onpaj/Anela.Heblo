namespace Anela.Heblo.Domain.Features.ProductPricing;

/// <summary>Append-only. No read method exists until something needs one.</summary>
public interface IProductPriceChangeLogRepository
{
    Task AppendAsync(ProductPriceChangeLog entry, CancellationToken ct);
}
