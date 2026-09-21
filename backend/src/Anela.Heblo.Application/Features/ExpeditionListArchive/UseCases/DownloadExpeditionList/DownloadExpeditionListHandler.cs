using Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;
using MediatR;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.DownloadExpeditionList;

public class DownloadExpeditionListHandler : IRequestHandler<DownloadExpeditionListRequest, DownloadExpeditionListResponse>
{
    private readonly IExpeditionListArchiveBlobStore _blobStore;
    private readonly string _containerName;

    public DownloadExpeditionListHandler(IExpeditionListArchiveBlobStore blobStore, IOptions<ExpeditionListArchiveOptions> options)
    {
        _blobStore = blobStore;
        _containerName = options.Value.BlobContainerName;
    }

    public async Task<DownloadExpeditionListResponse> Handle(DownloadExpeditionListRequest request, CancellationToken cancellationToken)
    {
        if (!BlobPathValidator.IsValid(request.BlobPath))
        {
            return DownloadExpeditionListResponse.Fail();
        }

        var stream = await _blobStore.DownloadAsync(_containerName, request.BlobPath, cancellationToken);
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
