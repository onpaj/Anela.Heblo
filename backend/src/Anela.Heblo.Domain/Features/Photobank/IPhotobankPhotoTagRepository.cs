using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Anela.Heblo.Domain.Features.Photobank
{
    public interface IPhotobankPhotoTagRepository
    {
        Task AddPhotoTagAsync(PhotoTag photoTag, CancellationToken cancellationToken);
        Task AddPhotoTagsAsync(IEnumerable<PhotoTag> photoTags, CancellationToken cancellationToken);
        Task RemovePhotoTagAsync(int photoId, int tagId, CancellationToken cancellationToken);
        Task<bool> PhotoTagExistsAsync(int photoId, int tagId, CancellationToken cancellationToken);
        Task RemoveRuleTagsAsync(string? scopeToTagName, CancellationToken cancellationToken);
        Task<HashSet<(int PhotoId, int TagId)>> GetOccupiedTagPairsAsync(string? scopeToTagName, CancellationToken cancellationToken);
        Task<List<PhotoTag>> GetPhotoTagsByPhotoAndSourceAsync(int photoId, PhotoTagSource source, CancellationToken cancellationToken);
        Task RemovePhotoTagsAsync(IEnumerable<PhotoTag> photoTags, CancellationToken cancellationToken);
        Task RemovePhotoTagsBySourceAsync(IReadOnlyList<int> photoIds, PhotoTagSource source, CancellationToken cancellationToken);

        Task SaveChangesAsync(CancellationToken cancellationToken);
    }
}
