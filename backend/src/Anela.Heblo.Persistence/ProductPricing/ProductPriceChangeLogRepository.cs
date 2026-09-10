using Anela.Heblo.Domain.Features.ProductPricing;
using Microsoft.EntityFrameworkCore;

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

        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch
        {
            // A failed SaveChanges leaves the Added entity tracked; left alone it would
            // resurface at a later, unrelated SaveChanges on this same (scoped) context and
            // get inserted then, attributed to whatever triggered that save. Detach it here so
            // the failure is fully contained to this call, then let the caller decide what to
            // do with the exception (SetProductPriceHandler logs and swallows it).
            _context.Entry(toPersist).State = EntityState.Detached;
            throw;
        }
    }
}
