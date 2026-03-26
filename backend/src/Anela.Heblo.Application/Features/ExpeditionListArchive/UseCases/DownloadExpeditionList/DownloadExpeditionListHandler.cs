using Anela.Heblo.Domain.Features.FileStorage;
using MediatR;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.DownloadExpeditionList;

public class DownloadExpeditionListHandler : IRequestHandler<DownloadExpeditionListRequest, DownloadExpeditionListResponse>
{
    private const string ContainerName = "expedition-lists";
    private readonly IBlobStorageService _blobStorageService;

    public DownloadExpeditionListHandler(IBlobStorageService blobStorageService)
    {
        _blobStorageService = blobStorageService;
    }

    public async Task<DownloadExpeditionListResponse> Handle(DownloadExpeditionListRequest request, CancellationToken cancellationToken)
    {
        if (!BlobPathValidator.IsValid(request.BlobPath))
        {
            return DownloadExpeditionListResponse.Fail("Invalid blob path.");
        }

        var stream = await _blobStorageService.DownloadAsync(ContainerName, request.BlobPath, cancellationToken);
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
