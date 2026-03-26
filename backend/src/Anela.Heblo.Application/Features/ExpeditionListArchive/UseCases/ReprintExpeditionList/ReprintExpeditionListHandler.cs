using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Anela.Heblo.Domain.Features.FileStorage;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.ReprintExpeditionList;

public class ReprintExpeditionListHandler : IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>
{
    private readonly IBlobStorageService _blobStorageService;
    private readonly IPrintQueueSink _printQueueSink;
    private readonly ILogger<ReprintExpeditionListHandler> _logger;

    public ReprintExpeditionListHandler(
        IBlobStorageService blobStorageService,
        IPrintQueueSink printQueueSink,
        ILogger<ReprintExpeditionListHandler> logger)
    {
        _blobStorageService = blobStorageService;
        _printQueueSink = printQueueSink;
        _logger = logger;
    }

    public async Task<ReprintExpeditionListResponse> Handle(ReprintExpeditionListRequest request, CancellationToken cancellationToken)
    {
        if (!ExpeditionListArchiveConstants.ValidBlobPathRegex.IsMatch(request.BlobPath))
        {
            throw new ArgumentException($"Invalid blob path: {request.BlobPath}");
        }

        _logger.LogInformation("Reprinting expedition list: {BlobPath}", request.BlobPath);

        var tempFile = Path.GetTempFileName();
        var pdfTempFile = Path.ChangeExtension(tempFile, ".pdf");

        try
        {
            await using var stream = await _blobStorageService.DownloadAsync(ExpeditionListArchiveConstants.ContainerName, request.BlobPath, cancellationToken);
            await using var fileStream = File.Create(pdfTempFile);
            await stream.CopyToAsync(fileStream, cancellationToken);
            fileStream.Close();

            await _printQueueSink.SendAsync([pdfTempFile], cancellationToken);

            _logger.LogInformation("Successfully reprinted: {BlobPath}", request.BlobPath);
            return new ReprintExpeditionListResponse { Success = true, Message = "Soubor byl odeslán na tiskárnu." };
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
            if (File.Exists(pdfTempFile)) File.Delete(pdfTempFile);
        }
    }
}
