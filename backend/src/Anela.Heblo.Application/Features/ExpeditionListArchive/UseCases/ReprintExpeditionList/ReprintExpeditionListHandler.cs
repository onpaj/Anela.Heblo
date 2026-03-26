using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Anela.Heblo.Domain.Features.FileStorage;
using MediatR;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.ReprintExpeditionList;

public class ReprintExpeditionListHandler : IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>
{
    private const string ContainerName = "expedition-lists";
    private readonly IBlobStorageService _blobStorageService;
    private readonly IPrintQueueSink _cupsSink;

    public ReprintExpeditionListHandler(IBlobStorageService blobStorageService, IPrintQueueSink cupsSink)
    {
        _blobStorageService = blobStorageService;
        _cupsSink = cupsSink;
    }

    public async Task<ReprintExpeditionListResponse> Handle(ReprintExpeditionListRequest request, CancellationToken cancellationToken)
    {
        if (!BlobPathValidator.IsValid(request.BlobPath))
        {
            return ReprintExpeditionListResponse.Fail("Invalid blob path.");
        }

        var tempFile = Path.GetTempFileName() + ".pdf";
        try
        {
            await using var blobStream = await _blobStorageService.DownloadAsync(ContainerName, request.BlobPath, cancellationToken);
            await using var fileStream = File.OpenWrite(tempFile);
            await blobStream.CopyToAsync(fileStream, cancellationToken);
        }
        catch
        {
            DeleteTempFile(tempFile);
            throw;
        }

        try
        {
            await _cupsSink.SendAsync(new[] { tempFile }, cancellationToken);
            return new ReprintExpeditionListResponse { Success = true };
        }
        finally
        {
            DeleteTempFile(tempFile);
        }
    }

    private static void DeleteTempFile(string path)
    {
        try { File.Delete(path); } catch { /* best effort */ }
    }
}
