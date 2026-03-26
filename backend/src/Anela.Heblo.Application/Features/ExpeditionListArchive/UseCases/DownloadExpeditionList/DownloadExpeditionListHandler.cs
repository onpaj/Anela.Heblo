using Anela.Heblo.Domain.Features.FileStorage;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.DownloadExpeditionList;

public class DownloadExpeditionListHandler : IRequestHandler<DownloadExpeditionListRequest, DownloadExpeditionListResponse>
{
    private const string ContainerName = "expedition-lists";
    private static readonly System.Text.RegularExpressions.Regex ValidBlobPathRegex =
        new(@"^\d{4}-\d{2}-\d{2}/[^/]+\.pdf$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private readonly IBlobStorageService _blobStorageService;
    private readonly ILogger<DownloadExpeditionListHandler> _logger;

    public DownloadExpeditionListHandler(IBlobStorageService blobStorageService, ILogger<DownloadExpeditionListHandler> logger)
    {
        _blobStorageService = blobStorageService;
        _logger = logger;
    }

    public async Task<DownloadExpeditionListResponse> Handle(DownloadExpeditionListRequest request, CancellationToken cancellationToken)
    {
        if (!ValidBlobPathRegex.IsMatch(request.BlobPath))
        {
            throw new ArgumentException($"Invalid blob path: {request.BlobPath}");
        }

        _logger.LogDebug("Downloading expedition list blob: {BlobPath}", request.BlobPath);

        var stream = await _blobStorageService.DownloadAsync(ContainerName, request.BlobPath, cancellationToken);
        var fileName = Path.GetFileName(request.BlobPath);

        return new DownloadExpeditionListResponse
        {
            Stream = stream,
            FileName = fileName,
            ContentType = "application/pdf",
        };
    }
}
