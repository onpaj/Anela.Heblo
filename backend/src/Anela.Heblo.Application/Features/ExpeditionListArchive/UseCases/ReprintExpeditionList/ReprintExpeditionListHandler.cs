using Anela.Heblo.Application.Shared.Printing;
using Anela.Heblo.Domain.Features.FileStorage;
using MediatR;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.ReprintExpeditionList;

public class ReprintExpeditionListHandler : IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>
{
    private readonly IBlobStorageService _blobStorageService;
    private readonly IPrintQueueSink _cupsSink;
    private readonly string _containerName;

    public ReprintExpeditionListHandler(IBlobStorageService blobStorageService, IPrintQueueSink cupsSink, IOptions<ExpeditionListArchiveOptions> options)
    {
        _blobStorageService = blobStorageService;
        _cupsSink = cupsSink;
        _containerName = options.Value.BlobContainerName;
    }

    public async Task<ReprintExpeditionListResponse> Handle(ReprintExpeditionListRequest request, CancellationToken cancellationToken)
    {
        if (!BlobPathValidator.IsValid(request.BlobPath))
        {
            return ReprintExpeditionListResponse.Fail("Invalid blob path.");
        }

        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf");
        try
        {
            await using var blobStream = await _blobStorageService.DownloadAsync(_containerName, request.BlobPath, cancellationToken);
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
