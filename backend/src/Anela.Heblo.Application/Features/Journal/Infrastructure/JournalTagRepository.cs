using Anela.Heblo.Application.Features.Journal.Contracts;
using Anela.Heblo.Domain.Features.Journal;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Repository;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Application.Features.Journal.Infrastructure
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