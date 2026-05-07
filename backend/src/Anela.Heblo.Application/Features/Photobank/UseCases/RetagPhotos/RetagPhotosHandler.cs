using Anela.Heblo.Application.Features.Photobank.Infrastructure.Jobs;
using Anela.Heblo.Domain.Features.Photobank;
using Anela.Heblo.Xcc.Services;
using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.RetagPhotos;

public class RetagPhotosHandler : IRequestHandler<RetagPhotosRequest, RetagPhotosResponse>
{
    private readonly IPhotobankRepository _repository;
    private readonly IBackgroundWorker _backgroundWorker;

    public RetagPhotosHandler(IPhotobankRepository repository, IBackgroundWorker backgroundWorker)
    {
        _repository = repository;
        _backgroundWorker = backgroundWorker;
    }

    public async Task<RetagPhotosResponse> Handle(RetagPhotosRequest request, CancellationToken cancellationToken)
    {
        var photos = await _repository.GetPhotosByIdsAsync(request.PhotoIds, cancellationToken);

        if (photos.Count == 0)
            return new RetagPhotosResponse { JobId = null };

        var foundIds = photos.Select(p => p.Id).ToList();

        await _repository.ResetAutoTaggedAtAsync(foundIds, cancellationToken);

        if (request.ClearExistingAiTags)
            await _repository.RemovePhotoTagsBySourceAsync(foundIds, PhotoTagSource.AI, cancellationToken);

        var candidates = photos
            .Select(p => new PhotoAutoTagCandidate(p.Id, p.FolderPath, p.FileName))
            .ToList();

        var jobId = _backgroundWorker.Enqueue<PhotobankAutoTagJob>(
            j => j.ExecuteForPhotosAsync(candidates, CancellationToken.None));

        return new RetagPhotosResponse { JobId = jobId };
    }
}
