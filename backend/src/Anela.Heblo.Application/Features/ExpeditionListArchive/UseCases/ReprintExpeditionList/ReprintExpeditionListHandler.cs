using Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;
using Anela.Heblo.Application.Shared.Printing;
using MediatR;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.ReprintExpeditionList;

public class ReprintExpeditionListHandler : IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>
{
    private readonly IExpeditionListArchiveBlobStore _blobStore;
    private readonly IPrintQueueSink _cupsSink;
    private readonly ITemporaryFileAccessor _temporaryFileAccessor;
    private readonly string _containerName;

    public ReprintExpeditionListHandler(
        IExpeditionListArchiveBlobStore blobStore,
        IPrintQueueSink cupsSink,
        ITemporaryFileAccessor temporaryFileAccessor,
        IOptions<ExpeditionListArchiveOptions> options)
    {
        _blobStore = blobStore;
        _cupsSink = cupsSink;
        _temporaryFileAccessor = temporaryFileAccessor;
        _containerName = options.Value.BlobContainerName;
    }

    public async Task<ReprintExpeditionListResponse> Handle(ReprintExpeditionListRequest request, CancellationToken cancellationToken)
    {
        if (!BlobPathValidator.IsValid(request.BlobPath))
        {
            return ReprintExpeditionListResponse.Fail();
        }

        string? tempFile = null;
        try
        {
            await using var blobStream = await _blobStore.DownloadAsync(_containerName, request.BlobPath, cancellationToken);
            tempFile = await _temporaryFileAccessor.CreateFromStreamAsync(blobStream, ".pdf", cancellationToken);

            await _cupsSink.SendAsync(new[] { tempFile }, cancellationToken);
            return new ReprintExpeditionListResponse { Success = true };
        }
        finally
        {
            if (tempFile != null)
            {
                _temporaryFileAccessor.DeleteIfExists(tempFile);
            }
        }
    }
}
