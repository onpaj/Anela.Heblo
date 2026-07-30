using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Domain.Features.Photobank;
using Anela.Heblo.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Photobank;

public class PhotobankTagRepository : IPhotobankTagRepository
{
    private readonly ApplicationDbContext _context;

    public PhotobankTagRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<TagCount>> GetTagsWithCountsAsync(CancellationToken cancellationToken)
    {
        var query =
            from t in _context.PhotobankTags
            join pt in _context.PhotoTags on t.Id equals pt.TagId into pts
            from pt in pts.DefaultIfEmpty()
            group pt by new { t.Id, t.Name } into g
            orderby g.Count(p => p != null) descending, g.Key.Name
            select new TagCount(g.Key.Id, g.Key.Name, g.Count(p => p != null));

        return await query.AsNoTracking().ToListAsync(cancellationToken);
    }

    private Task<Tag?> FindTagByNameAsync(string normalizedName, CancellationToken ct)
    {
        return _context.PhotobankTags
            .FirstOrDefaultAsync(t => t.Name == normalizedName, ct);
    }

    public async Task<Tag?> GetOrCreateTagAsync(string normalizedName, CancellationToken cancellationToken)
    {
        var existing = await FindTagByNameAsync(normalizedName, cancellationToken);

        if (existing != null)
            return existing;

        var tag = new Tag { Name = normalizedName };
        _context.PhotobankTags.Add(tag);
        await _context.SaveChangesAsync(cancellationToken);
        return tag;
    }

    public async Task<IReadOnlyDictionary<string, int>> GetOrCreateTagsAsync(
        IReadOnlyCollection<string> normalizedNames, CancellationToken cancellationToken)
    {
        var tagsByName = await _context.PhotobankTags
            .Where(t => normalizedNames.Contains(t.Name))
            .ToDictionaryAsync(t => t.Name, cancellationToken);

        var newTagsCreated = false;
        foreach (var name in normalizedNames.Where(n => !tagsByName.ContainsKey(n)))
        {
            var newTag = new Tag { Name = name };
            _context.PhotobankTags.Add(newTag);
            tagsByName[name] = newTag;
            newTagsCreated = true;
        }

        // Flush new Tag inserts so they receive DB-assigned IDs before use.
        if (newTagsCreated)
            await _context.SaveChangesAsync(cancellationToken);

        return tagsByName.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Id);
    }

    public async Task<Tag?> GetTagByIdAsync(int id, CancellationToken cancellationToken)
    {
        return await _context.PhotobankTags
            .Include(t => t.PhotoTags)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    public Task<Tag?> GetTagByNameAsync(string normalizedName, CancellationToken cancellationToken)
    {
        return FindTagByNameAsync(normalizedName, cancellationToken);
    }

    public Task DeleteTagAsync(Tag tag, CancellationToken cancellationToken)
    {
        _context.PhotobankTags.Remove(tag);
        return Task.CompletedTask;
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        await _context.SaveChangesAsync(cancellationToken);
    }
}
