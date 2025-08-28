using Anela.Heblo.Xcc.Infrastructure;
using Anela.Heblo.Persistence.Repository;
using Anela.Heblo.Xcc.Persistance;
using Anela.Heblo.Xcc.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Anela.Heblo.Persistence;

/// <summary>
/// Unit of Work implementation that coordinates work across multiple repositories
/// and provides transactional consistency
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _context;
    private readonly Func<Type, object?> _repositoryFactory;
    private readonly Dictionary<Type, object> _repositories;
    private IDbContextTransaction? _transaction;
    private bool _disposed;
    private bool _aborted;

    public UnitOfWork(ApplicationDbContext context, Func<Type, object?> repositoryFactory)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _repositoryFactory = repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory));
        _repositories = new Dictionary<Type, object>();
    }

    public IRepository<TEntity, TKey> Repository<TEntity, TKey>()
        where TEntity : class, IEntity<TKey>
    {
        var type = typeof(TEntity);

        if (_repositories.ContainsKey(type))
        {
            return (IRepository<TEntity, TKey>)_repositories[type];
        }

        // Try to get from factory first, fallback to creating BaseRepository
        var repository = _repositoryFactory(typeof(IRepository<TEntity, TKey>)) as IRepository<TEntity, TKey>
                      ?? new BaseRepository<TEntity, TKey>(_context);

        _repositories[type] = repository;
        return repository;
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Handle concurrency conflicts
            throw;
        }
        catch (DbUpdateException)
        {
            // Handle database update errors
            throw;
        }
    }

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction != null)
        {
            throw new InvalidOperationException("Transaction already started");
        }

        _transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction == null)
        {
            throw new InvalidOperationException("No transaction to commit");
        }

        try
        {
            await _transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction == null)
        {
            throw new InvalidOperationException("No transaction to rollback");
        }

        try
        {
            await _transaction.RollbackAsync(cancellationToken);
        }
        finally
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public void Abort()
    {
        _aborted = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            // Automatically save changes unless explicitly aborted
            if (!_aborted)
            {
                await SaveChangesAsync();
            }

            if (_transaction != null)
            {
                await _transaction.DisposeAsync();
                _transaction = null;
            }

            _disposed = true;
        }
    }

    public void Dispose()
    {
        DisposeAsync().GetAwaiter().GetResult();
    }
}