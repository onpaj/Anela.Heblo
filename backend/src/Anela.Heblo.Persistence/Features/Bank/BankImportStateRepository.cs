using Anela.Heblo.Domain.Features.Bank;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Features.Bank;

public class BankImportStateRepository : IBankImportStateRepository
{
    private readonly ApplicationDbContext _context;

    public BankImportStateRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<BankImportState?> GetByAccountAsync(string account, CancellationToken cancellationToken = default)
        => await _context.BankImportStates.FirstOrDefaultAsync(s => s.Account == account, cancellationToken);

    public async Task<IReadOnlyList<BankImportState>> GetAllAsync(CancellationToken cancellationToken = default)
        => await _context.BankImportStates.AsNoTracking().ToListAsync(cancellationToken);

    public async Task UpsertAsync(BankImportState state, CancellationToken cancellationToken = default)
    {
        var existing = await _context.BankImportStates
            .FirstOrDefaultAsync(s => s.Account == state.Account, cancellationToken);

        if (existing == null)
        {
            await _context.BankImportStates.AddAsync(state, cancellationToken);
        }
        // When `state` was loaded via GetByAccountAsync it is already tracked; EF detects changes.

        await _context.SaveChangesAsync(cancellationToken);
    }
}
