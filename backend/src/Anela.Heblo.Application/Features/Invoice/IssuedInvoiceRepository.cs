using Anela.Heblo.Domain.Features.Invoice;
using Anela.Heblo.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Invoice;

public class IssuedInvoiceRepository : IIssuedInvoiceRepository
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<IssuedInvoiceRepository> _logger;

    public IssuedInvoiceRepository(
        ApplicationDbContext context,
        ILogger<IssuedInvoiceRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ImportAttempt> RecordImportAttemptAsync(ImportAttempt attempt)
    {
        _logger.LogDebug("Recording import attempt for invoice {ExternalId}", attempt.ExternalInvoiceId);
        
        _context.ImportAttempts.Add(attempt);
        await _context.SaveChangesAsync();
        
        return attempt;
    }

    public async Task<List<ImportAttempt>> GetImportHistoryAsync(string externalId)
    {
        return await _context.ImportAttempts
            .Where(a => a.ExternalInvoiceId == externalId)
            .OrderByDescending(a => a.AttemptedAt)
            .ToListAsync();
    }

    public async Task<bool> IsSuccessfullyImportedAsync(string externalId)
    {
        return await _context.ImportAttempts
            .AnyAsync(a => a.ExternalInvoiceId == externalId && a.IsSuccess);
    }

    public async Task<List<IssuedInvoice>> GetImportedInvoicesAsync(int page, int pageSize)
    {
        var skip = (page - 1) * pageSize;
        
        return await _context.IssuedInvoices
            .Include(i => i.ImportAttempts)
            .OrderByDescending(i => i.CreatedAt)
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<List<IssuedInvoice>> SearchInvoicesAsync(string searchTerm)
    {
        var term = searchTerm.ToLower();
        
        return await _context.IssuedInvoices
            .Include(i => i.ImportAttempts)
            .Where(i => 
                i.InvoiceNumber.ToLower().Contains(term) ||
                i.CustomerName.ToLower().Contains(term) ||
                (i.CustomerEmail != null && i.CustomerEmail.ToLower().Contains(term)) ||
                (i.Description != null && i.Description.ToLower().Contains(term))
            )
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();
    }

    public async Task<IssuedInvoice?> GetInvoiceDetailAsync(string externalId)
    {
        return await _context.IssuedInvoices
            .Include(i => i.ImportAttempts)
            .FirstOrDefaultAsync(i => i.ExternalId == externalId);
    }

    public async Task<int> GetTotalInvoicesCountAsync()
    {
        return await _context.IssuedInvoices.CountAsync();
    }
}