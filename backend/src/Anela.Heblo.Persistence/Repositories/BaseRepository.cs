using System.Linq.Expressions;
using Anela.Heblo.Xcc.Domain;
using Anela.Heblo.Xcc.Persistance;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Repositories;

/// <summary>
/// Base generic repository implementation using Entity Framework Core
/// </summary>
/// <typeparam name="TEntity">The entity type</typeparam>
/// <typeparam name="TKey">Entity unique key</typeparam>
public class BaseRepository<TEntity, TKey> : IRepository<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
{
    protected readonly ApplicationDbContext Context;
    protected readonly DbSet<TEntity> DbSet;

    public BaseRepository(ApplicationDbContext context)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        DbSet = context.Set<TEntity>();
    }

    public virtual async Task<TEntity?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default)
    {
        return await DbSet.FindAsync(id, cancellationToken);
    }

    public virtual async Task<IEnumerable<TEntity>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await DbSet.ToListAsync(cancellationToken);
    }

    public virtual async Task<IEnumerable<TEntity>> FindAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return await DbSet.Where(predicate).ToListAsync(cancellationToken);
    }

    public virtual async Task<TEntity?> SingleOrDefaultAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return await DbSet.SingleOrDefaultAsync(predicate, cancellationToken);
    }

    public virtual async Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return await DbSet.AnyAsync(predicate, cancellationToken);
    }

    public virtual async Task<int> CountAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default)
    {
        return predicate == null
            ? await DbSet.CountAsync(cancellationToken)
            : await DbSet.CountAsync(predicate, cancellationToken);
    }

    public virtual async Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        var result = await DbSet.AddAsync(entity, cancellationToken);
        return result.Entity;
    }

    public virtual async Task<IEnumerable<TEntity>> AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        var entityList = entities.ToList();
        await DbSet.AddRangeAsync(entityList, cancellationToken);
        return entityList;
    }

    public virtual Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        DbSet.Update(entity);
        return Task.CompletedTask;
    }

    public virtual async Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        DbSet.Remove(entity);
        await Task.CompletedTask;
    }

    public virtual async Task DeleteAsync(TKey id, CancellationToken cancellationToken = default)
    {
        var entity = await GetByIdAsync(id, cancellationToken);
        if (entity != null)
        {
            await DeleteAsync(entity, cancellationToken);
        }
    }

    public virtual async Task DeleteRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        DbSet.RemoveRange(entities);
        await Task.CompletedTask;
    }

    public virtual async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await Context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Runs <paramref name="operation"/> inside a single all-or-nothing database transaction,
    /// opened via <see cref="Microsoft.EntityFrameworkCore.Storage.IExecutionStrategy"/> so it is
    /// safe under a retrying execution strategy (e.g. PollyExecutionStrategy) — EF Core forbids
    /// calling BeginTransactionAsync directly when the configured strategy retries on failure.
    /// Everything the transaction must cover (entity adds, SaveChangesAsync calls) must happen
    /// inside <paramref name="operation"/>, because each attempt starts from an empty change
    /// tracker: EF Core does <b>not</b> reset the tracker between execution-strategy attempts, so
    /// this method clears it explicitly before every attempt. Without that, a transient failure
    /// part-way through a multi-<see cref="SaveChangesAsync"/> delegate would leave the already
    /// accepted entities <c>Unchanged</c> (never re-inserted after the rollback) and the failed
    /// one <c>Added</c> (re-inserted on the next attempt carrying its stale, rolled-back keys).
    /// The flip side is that <paramref name="operation"/> must re-create everything it writes on
    /// every invocation, and that any entity tracked by the caller <i>before</i> this call is
    /// discarded — never stage work outside the delegate. This wraps the entire underlying
    /// DbContext, not just <typeparamref name="TEntity"/> — the same whole-context semantics
    /// <see cref="SaveChangesAsync"/> already has.
    /// </summary>
    public virtual async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        var strategy = Context.Database.CreateExecutionStrategy();

        // The token-carrying overload is required: it hands the caller's token to the strategy
        // (so an aborted request cancels the retry loop and its backoff delays) and gives the
        // delegate the per-attempt token the strategy's timeout cancels — a captured outer token
        // would let a timed-out attempt keep running against this same scoped DbContext while the
        // next attempt started on it.
        return await strategy.ExecuteAsync(async ct =>
        {
            // See the remarks above: EF Core does not do this for us, and a retry that reuses the
            // previous attempt's tracked state commits rows referencing a rolled-back parent.
            Context.ChangeTracker.Clear();

            // `await using` rolls an uncommitted transaction back on dispose. There is deliberately
            // no catch/RollbackAsync here: on the broken-connection failures this strategy exists
            // for, RollbackAsync itself throws and would replace the original exception, which both
            // breaks the "exception surfaced unchanged" contract and hides the real error from the
            // strategy's transient classification.
            await using var transaction = await Context.Database.BeginTransactionAsync(ct);

            var result = await operation(ct);
            await transaction.CommitAsync(ct);
            return result;
        }, cancellationToken);
    }
}