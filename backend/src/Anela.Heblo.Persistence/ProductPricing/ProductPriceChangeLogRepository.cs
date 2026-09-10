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
        if (entry.ErrorMessage is { Length: > ErrorMessageMaxLength })
        {
            entry.ErrorMessage = entry.ErrorMessage[..ErrorMessageMaxLength];
        }

        _context.ProductPriceChangeLogs.Add(entry);
        await _context.SaveChangesAsync(ct);
    }
}
