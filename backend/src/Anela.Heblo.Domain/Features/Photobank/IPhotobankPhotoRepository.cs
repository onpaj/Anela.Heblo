using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Anela.Heblo.Domain.Features.Photobank
{
    public sealed record PhotoLocator(string DriveId, string SharePointFileId, DateTime ModifiedAt);

    public interface IPhotobankPhotoRepository
    {
        Task<(List<Photo> Items, int Total)> GetPhotosAsync(
            List<string>? tags, string? search, bool useRegex, bool withoutTags, int page, int pageSize,
            CancellationToken cancellationToken);

        Task<int> CountFilteredPhotosAsync(List<string>? tags, string? search, CancellationToken cancellationToken);

        Task<List<int>> GetFilteredPhotoIdsMissingTagAsync(List<string>? tags, string? search, int tagId, CancellationToken cancellationToken);

        Task<List<int>> GetExistingPhotoIdsMissingTagAsync(IReadOnlyList<int> photoIds, int tagId, CancellationToken cancellationToken);

        Task<int> CountExistingPhotosAsync(IReadOnlyList<int> photoIds, CancellationToken cancellationToken);

        Task<Photo?> GetPhotoByIdAsync(int id, CancellationToken cancellationToken);

        Task<PhotoLocator?> GetLocatorAsync(int id, CancellationToken cancellationToken);

        Task<Photo?> GetPhotoBySharePointFileIdAsync(string sharePointFileId, CancellationToken cancellationToken);

        Task AddPhotoAsync(Photo photo, CancellationToken cancellationToken);

        Task RemovePhotoAsync(Photo photo, CancellationToken cancellationToken);

        Task<List<Photo>> GetPhotosByIdsAsync(IReadOnlyList<int> photoIds, CancellationToken cancellationToken);

        Task SaveChangesAsync(CancellationToken cancellationToken);
    }
}
