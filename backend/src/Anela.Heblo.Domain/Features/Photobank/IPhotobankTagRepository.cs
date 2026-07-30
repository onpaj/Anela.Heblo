using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Anela.Heblo.Domain.Features.Photobank
{
    public interface IPhotobankTagRepository
    {
        Task<IReadOnlyList<TagCount>> GetTagsWithCountsAsync(CancellationToken cancellationToken);
        Task<Tag?> GetOrCreateTagAsync(string normalizedName, CancellationToken cancellationToken);
        Task<IReadOnlyDictionary<string, int>> GetOrCreateTagsAsync(IReadOnlyCollection<string> normalizedNames, CancellationToken cancellationToken);
        Task<Tag?> GetTagByIdAsync(int id, CancellationToken cancellationToken);
        Task<Tag?> GetTagByNameAsync(string normalizedName, CancellationToken cancellationToken);
        Task DeleteTagAsync(Tag tag, CancellationToken cancellationToken);

        Task SaveChangesAsync(CancellationToken cancellationToken);
    }
}
