using System.Linq.Expressions;
using Anela.Heblo.Domain.Features.Invoices;

namespace Anela.Heblo.Tests.Features.PackingMaterials;

public class MockIssuedInvoiceRepository : IIssuedInvoiceRepository
{
    private List<IssuedInvoice> _invoices = new();

    public void SetInvoices(IEnumerable<IssuedInvoice> invoices)
    {
        _invoices = new List<IssuedInvoice>(invoices);
    }

    public Task<IEnumerable<IssuedInvoice>> GetHeadersByDateAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        var result = _invoices.Where(i => DateOnly.FromDateTime(i.InvoiceDate) == date);
        return Task.FromResult<IEnumerable<IssuedInvoice>>(result.ToList());
    }

    public Task<IssuedInvoice?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<IEnumerable<IssuedInvoice>> GetAllAsync(CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<IEnumerable<IssuedInvoice>> FindAsync(Expression<Func<IssuedInvoice, bool>> predicate, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<IssuedInvoice?> SingleOrDefaultAsync(Expression<Func<IssuedInvoice, bool>> predicate, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<bool> AnyAsync(Expression<Func<IssuedInvoice, bool>> predicate, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<int> CountAsync(Expression<Func<IssuedInvoice, bool>>? predicate = null, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<IssuedInvoice> AddAsync(IssuedInvoice entity, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<IEnumerable<IssuedInvoice>> AddRangeAsync(IEnumerable<IssuedInvoice> entities, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task UpdateAsync(IssuedInvoice entity, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task DeleteAsync(IssuedInvoice entity, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task DeleteRangeAsync(IEnumerable<IssuedInvoice> entities, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<IEnumerable<IssuedInvoice>> FindBySyncStatusAsync(bool? isSynced, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<IEnumerable<IssuedInvoice>> FindByInvoiceDateRangeAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<IEnumerable<IssuedInvoice>> FindByCustomerNameAsync(string customerName, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<IEnumerable<IssuedInvoice>> FindWithCriticalErrorsAsync(CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<IssuedInvoice?> GetByIdWithSyncHistoryAsync(string id, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<IEnumerable<IssuedInvoice>> FindStaleInvoicesAsync(DateTime beforeDate, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<IssuedInvoiceSyncStats> GetSyncStatsAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<PaginatedResult<IssuedInvoice>> GetPaginatedAsync(IssuedInvoiceFilters filters, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}
