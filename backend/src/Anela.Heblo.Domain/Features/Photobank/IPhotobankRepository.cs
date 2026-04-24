using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Anela.Heblo.Domain.Features.Photobank
{
    public interface IPhotobankRepository
    {
        // Photos
        Task<(List<Photo> Items, int Total)> GetPhotosAsync(
            List<string>? tags, string? search, int page, int pageSize,
            CancellationToken cancellationToken);

        Task<Photo?> GetPhotoByIdAsync(int id, CancellationToken cancellationToken);

        // Tags
        Task<List<(Tag Tag, int Count)>> GetTagsWithCountsAsync(CancellationToken cancellationToken);
        Task<Tag?> GetOrCreateTagAsync(string normalizedName, CancellationToken cancellationToken);
        Task<Tag?> GetTagByIdAsync(int id, CancellationToken cancellationToken);

        // Photo tags
        Task AddPhotoTagAsync(PhotoTag photoTag, CancellationToken cancellationToken);
        Task RemovePhotoTagAsync(int photoId, int tagId, CancellationToken cancellationToken);
        Task<bool> PhotoTagExistsAsync(int photoId, int tagId, CancellationToken cancellationToken);

        // Roots
        Task<List<PhotobankIndexRoot>> GetRootsAsync(CancellationToken cancellationToken);
        Task<PhotobankIndexRoot> AddRootAsync(PhotobankIndexRoot root, CancellationToken cancellationToken);
        Task<bool> DeleteRootAsync(int id, CancellationToken cancellationToken);

        // Rules
        Task<List<TagRule>> GetRulesAsync(CancellationToken cancellationToken);
        Task<TagRule> AddRuleAsync(TagRule rule, CancellationToken cancellationToken);
        Task<TagRule?> GetRuleByIdAsync(int id, CancellationToken cancellationToken);
        Task UpdateRuleAsync(TagRule rule, CancellationToken cancellationToken);
        Task<bool> DeleteRuleAsync(int id, CancellationToken cancellationToken);

        // Reapply rules
        Task<int> ReapplyRulesAsync(List<TagRule> activeRules, CancellationToken cancellationToken);

        Task SaveChangesAsync(CancellationToken cancellationToken);
    }
}
