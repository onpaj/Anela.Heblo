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
        else if (!ReferenceEquals(existing, state))
        {
            throw new InvalidOperationException(
                $"UpsertAsync requires a tracked entity for account '{state.Account}'. " +
                "Load the existing state via GetByAccountAsync before mutating and upserting.");
        }
        // else: state is the tracked entity returned by GetByAccountAsync; EF snapshot change
        // tracking detects mutations from RecordSuccess/RecordFailure automatically.

        await _context.SaveChangesAsync(cancellationToken);
    }
}
