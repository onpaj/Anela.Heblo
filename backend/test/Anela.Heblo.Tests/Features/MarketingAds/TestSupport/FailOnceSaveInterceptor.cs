using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Anela.Heblo.Tests.Features.MarketingAds.TestSupport;

/// <summary>Fails the first SaveChanges that would write a matching entity, then lets every save through.</summary>
internal sealed class FailOnceSaveInterceptor<TEntity> : SaveChangesInterceptor where TEntity : class
{
    private readonly Func<TEntity, bool> _match;
    private bool _failed;

    public FailOnceSaveInterceptor(Func<TEntity, bool> match) => _match = match;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (!_failed && eventData.Context!.ChangeTracker.Entries<TEntity>().Any(e => e.State == EntityState.Added && _match(e.Entity)))
        {
            _failed = true;
            throw new DbUpdateException("simulated save failure");
        }
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
