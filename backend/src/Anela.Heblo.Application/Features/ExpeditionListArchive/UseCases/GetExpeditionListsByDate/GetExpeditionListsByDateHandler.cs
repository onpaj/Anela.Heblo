using Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;
using Anela.Heblo.Domain.Features.FileStorage;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.GetExpeditionListsByDate;

public class GetExpeditionListsByDateHandler : IRequestHandler<GetExpeditionListsByDateRequest, GetExpeditionListsByDateResponse>
{
    private readonly IBlobStorageService _blobStorageService;
    private readonly ILogger<GetExpeditionListsByDateHandler> _logger;

    public GetExpeditionListsByDateHandler(IBlobStorageService blobStorageService, ILogger<GetExpeditionListsByDateHandler> logger)
    {
        _blobStorageService = blobStorageService;
        _logger = logger;
    }

    public async Task<GetExpeditionListsByDateResponse> Handle(GetExpeditionListsByDateRequest request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Fetching expedition lists for date {Date}", request.Date);

        var prefix = $"{request.Date}/";
        var blobs = await _blobStorageService.ListBlobsAsync(ExpeditionListArchiveConstants.ContainerName, prefix, cancellationToken);

        var items = blobs
            .Select(b => new ExpeditionListItemDto
            {
                FileName = b.FileName,
                BlobPath = b.Name,
                UploadedAt = b.CreatedOn,
                SizeBytes = b.ContentLength,
            })
            .OrderBy(i => i.UploadedAt)
            .ToList();

        return new GetExpeditionListsByDateResponse
        {
            Items = items,
            Date = request.Date,
        };
    }
}
