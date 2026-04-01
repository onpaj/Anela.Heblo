using Anela.Heblo.Application.Features.ExpeditionList;
using Anela.Heblo.Domain.Features.FileStorage;
using MediatR;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.DownloadExpeditionList;

public class DownloadExpeditionListHandler : IRequestHandler<DownloadExpeditionListRequest, DownloadExpeditionListResponse>
{
    private readonly IBlobStorageService _blobStorageService;
    private readonly string _containerName;

    public DownloadExpeditionListHandler(IBlobStorageService blobStorageService, IOptions<PrintPickingListOptions> options)
    {
        _blobStorageService = blobStorageService;
        _containerName = options.Value.BlobContainerName;
    }

    public async Task<DownloadExpeditionListResponse> Handle(DownloadExpeditionListRequest request, CancellationToken cancellationToken)
    {
        if (!BlobPathValidator.IsValid(request.BlobPath))
        {
            return DownloadExpeditionListResponse.Fail("Invalid blob path.");
        }

        var stream = await _blobStorageService.DownloadAsync(_containerName, request.BlobPath, cancellationToken);
        var fileName = Path.GetFileName(request.BlobPath);

        return new DownloadExpeditionListResponse
        {
            Success = true,
            Stream = stream,
            ContentType = "application/pdf",
            FileName = fileName
        };
    }
}
