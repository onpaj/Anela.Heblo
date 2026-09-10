using Anela.Heblo.Domain.Features.ProductPricing;

namespace Anela.Heblo.Persistence.ProductPricing;

public class ProductPriceChangeLogRepository : IProductPriceChangeLogRepository
{
    /// <summary>Column is varchar(2000); an untruncated remote error body would fail the insert.</summary>
    private const int ErrorMessageMaxLength = 2000;

    private readonly ApplicationDbContext _context;

    public ProductPriceChangeLogRepository(ApplicationDbContext context) => _context = context;

    public async Task AppendAsync(ProductPriceChangeLog entry, CancellationToken ct)
    {
        var errorMessage = entry.ErrorMessage is { Length: > ErrorMessageMaxLength }
            ? entry.ErrorMessage[..ErrorMessageMaxLength]
            : entry.ErrorMessage;

        var toPersist = new ProductPriceChangeLog
        {
            ProductCode = entry.ProductCode,
            OldPriceWithVat = entry.OldPriceWithVat,
            NewPriceWithVat = entry.NewPriceWithVat,
            ChangedAt = entry.ChangedAt,
            ChangedBy = entry.ChangedBy,
            ShoptetSucceeded = entry.ShoptetSucceeded,
            FlexiSucceeded = entry.FlexiSucceeded,
            ErrorMessage = errorMessage,
        };

        _context.ProductPriceChangeLogs.Add(toPersist);
        await _context.SaveChangesAsync(ct);
    }
}
