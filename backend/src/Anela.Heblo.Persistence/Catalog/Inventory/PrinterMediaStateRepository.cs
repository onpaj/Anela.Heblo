using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Catalog.Inventory;

public sealed class PrinterMediaStateRepository : IPrinterMediaStateRepository
{
    private readonly ApplicationDbContext _context;

    public PrinterMediaStateRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PrinterMediaState> GetAsync(CancellationToken cancellationToken = default)
    {
        return await _context.PrinterMediaStates.FirstOrDefaultAsync(cancellationToken)
            ?? PrinterMediaState.CreateDefault();
    }

    public async Task SaveAsync(PrinterMediaState state, CancellationToken cancellationToken = default)
    {
        var existing = await _context.PrinterMediaStates.FirstOrDefaultAsync(cancellationToken);
        if (existing is null)
        {
            _context.PrinterMediaStates.Add(state);
        }
        else
        {
            existing.RecordPrint(state.LastMediaType, state.LastPrintedBy ?? string.Empty);
        }
        await _context.SaveChangesAsync(cancellationToken);
    }
}
