using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Domain.Features.Journal;
using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Application.Features.Journal.Contracts
{
    public interface IJournalTagRepository : IRepository<JournalEntryTag, int>
    {
        Task<List<JournalEntryTag>> GetAllTagsAsync(CancellationToken cancellationToken = default);
    }
}