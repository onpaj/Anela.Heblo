using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Anela.Heblo.Domain.Features.Photobank
{
    public sealed record PhotoLocator(string DriveId, string SharePointFileId, DateTime ModifiedAt);

    public interface IPhotobankRepository
    {
        // Photos
        Task<(List<Photo> Items, int Total)> GetPhotosAsync(
            List<string>? tags, string? search, bool useRegex, bool withoutTags, int page, int pageSize,
            CancellationToken cancellationToken);

        Task<int> CountFilteredPhotosAsync(List<string>? tags, string? search, CancellationToken cancellationToken);

        Task<List<int>> GetFilteredPhotoIdsMissingTagAsync(List<string>? tags, string? search, int tagId, CancellationToken cancellationToken);

        Task<List<int>> GetExistingPhotoIdsMissingTagAsync(IReadOnlyList<int> photoIds, int tagId, CancellationToken cancellationToken);

        Task<int> CountExistingPhotosAsync(IReadOnlyList<int> photoIds, CancellationToken cancellationToken);

        Task<Photo?> GetPhotoByIdAsync(int id, CancellationToken cancellationToken);

        Task<PhotoLocator?> GetLocatorAsync(int id, CancellationToken cancellationToken);

        Task<List<Photo>> GetAllPhotosAsync(CancellationToken cancellationToken);

        // Tags
        Task<IReadOnlyList<TagCount>> GetTagsWithCountsAsync(CancellationToken cancellationToken);
        Task<Tag?> GetOrCreateTagAsync(string normalizedName, CancellationToken cancellationToken);
        Task<Tag?> GetTagByIdAsync(int id, CancellationToken cancellationToken);
        Task<Tag?> GetTagByNameAsync(string normalizedName, CancellationToken cancellationToken);
        Task DeleteTagAsync(Tag tag, CancellationToken cancellationToken);

        // Photo tags
        Task AddPhotoTagAsync(PhotoTag photoTag, CancellationToken cancellationToken);
        Task RemovePhotoTagAsync(int photoId, int tagId, CancellationToken cancellationToken);
        Task<bool> PhotoTagExistsAsync(int photoId, int tagId, CancellationToken cancellationToken);
        Task RemoveRuleTagsAsync(string? scopeToTagName, CancellationToken cancellationToken);

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
        Task<int> ReapplyRulesAsync(List<TagRule> allRules, string? scopeToTagName, CancellationToken cancellationToken);

        // Auto-tagging
        Task<List<PhotoAutoTagCandidate>> GetPhotosPendingAutoTagAsync(int pageSize, int offset, CancellationToken cancellationToken);
        Task StampAutoTaggedAtAsync(IReadOnlyList<int> photoIds, DateTime timestamp, CancellationToken cancellationToken);
        Task ResetAutoTaggedAtAsync(IReadOnlyList<int> photoIds, CancellationToken cancellationToken);
        Task<List<Photo>> GetPhotosByIdsAsync(IReadOnlyList<int> photoIds, CancellationToken cancellationToken);
        Task RemovePhotoTagsBySourceAsync(IReadOnlyList<int> photoIds, PhotoTagSource source, CancellationToken cancellationToken);

        Task SaveChangesAsync(CancellationToken cancellationToken);
    }
}
