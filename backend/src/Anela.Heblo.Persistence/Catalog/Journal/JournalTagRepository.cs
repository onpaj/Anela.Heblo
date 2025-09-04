using Anela.Heblo.Domain.Features.Journal;
using Anela.Heblo.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Catalog.Journal
{
    public class JournalTagRepository : BaseRepository<JournalEntryTag, int>, IJournalTagRepository
    {
        public JournalTagRepository(ApplicationDbContext context)
            : base(context)
        {
        }

        public async Task<List<JournalEntryTag>> GetAllTagsAsync(CancellationToken cancellationToken = default)
        {
            return await Context.Set<JournalEntryTag>()
                .OrderBy(x => x.Name)
                .ToListAsync(cancellationToken);
        }
    }
}