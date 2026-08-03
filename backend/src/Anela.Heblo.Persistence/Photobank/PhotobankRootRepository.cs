using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Domain.Features.Photobank;
using Anela.Heblo.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Photobank;

public class PhotobankRootRepository : IPhotobankRootRepository
{
    private readonly ApplicationDbContext _context;

    public PhotobankRootRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<PhotobankIndexRoot>> GetRootsAsync(CancellationToken cancellationToken)
    {
        return await _context.PhotobankIndexRoots
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<PhotobankIndexRoot>> GetActiveRootsWithDriveAsync(CancellationToken cancellationToken)
    {
        return await _context.PhotobankIndexRoots
            .Where(r => r.IsActive && r.DriveId != null)
            .ToListAsync(cancellationToken);
    }

    public Task<PhotobankIndexRoot> AddRootAsync(PhotobankIndexRoot root, CancellationToken cancellationToken)
    {
        _context.PhotobankIndexRoots.Add(root);
        return Task.FromResult(root);
    }

    public async Task<bool> DeleteRootAsync(int id, CancellationToken cancellationToken)
    {
        var root = await _context.PhotobankIndexRoots.FindAsync(new object[] { id }, cancellationToken);
        if (root == null)
            return false;
        _context.PhotobankIndexRoots.Remove(root);
        return true;
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        await _context.SaveChangesAsync(cancellationToken);
    }
}
