using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Domain.Features.Photobank;
using Anela.Heblo.Xcc.Services;
using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.RetagPhotos;

public class RetagPhotosHandler : IRequestHandler<RetagPhotosRequest, RetagPhotosResponse>
{
    private readonly IPhotobankPhotoRepository _photoRepository;
    private readonly IPhotobankPhotoTagRepository _photoTagRepository;
    private readonly IPhotobankAutoTagRepository _autoTagRepository;
    private readonly IBackgroundWorker _backgroundWorker;
    private readonly IPhotobankTagsCache _cache;

    public RetagPhotosHandler(
        IPhotobankPhotoRepository photoRepository,
        IPhotobankPhotoTagRepository photoTagRepository,
        IPhotobankAutoTagRepository autoTagRepository,
        IBackgroundWorker backgroundWorker,
        IPhotobankTagsCache cache)
    {
        _photoRepository = photoRepository;
        _photoTagRepository = photoTagRepository;
        _autoTagRepository = autoTagRepository;
        _backgroundWorker = backgroundWorker;
        _cache = cache;
    }

    public async Task<RetagPhotosResponse> Handle(RetagPhotosRequest request, CancellationToken cancellationToken)
    {
        var photos = await _photoRepository.GetPhotosByIdsAsync(request.PhotoIds, cancellationToken);

        if (photos.Count == 0)
            return new RetagPhotosResponse { JobId = null };

        var foundIds = photos.Select(p => p.Id).ToList();

        await _autoTagRepository.ResetAutoTaggedAtAsync(foundIds, cancellationToken);

        if (request.ClearExistingAiTags)
            await _photoTagRepository.RemovePhotoTagsBySourceAsync(foundIds, PhotoTagSource.AI, cancellationToken);

        _cache.Invalidate();

        var candidates = photos
            .Select(p => new PhotoAutoTagCandidate(p.Id, p.FolderPath, p.FileName))
            .ToList();

        var jobId = _backgroundWorker.Enqueue<PhotobankAutoTagJob>(
            j => j.ExecuteForPhotosAsync(candidates, CancellationToken.None));

        return new RetagPhotosResponse { JobId = jobId };
    }
}
