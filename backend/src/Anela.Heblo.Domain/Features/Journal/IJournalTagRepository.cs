using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Domain.Features.Journal
{
    public interface IJournalTagRepository : IRepository<JournalEntryTag, int>
    {
        Task<List<JournalEntryTag>> GetAllTagsAsync(CancellationToken cancellationToken = default);
    }
}