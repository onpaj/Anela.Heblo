using Anela.Heblo.Domain.Features.FileStorage;
using MediatR;
using System.Text.RegularExpressions;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.DownloadExpeditionList;

public class DownloadExpeditionListHandler : IRequestHandler<DownloadExpeditionListRequest, DownloadExpeditionListResponse>
{
    private const string ContainerName = "expedition-lists";
    private static readonly Regex ValidBlobPathPattern = new(@"^\d{4}-\d{2}-\d{2}/[^/]+\.pdf$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private readonly IBlobStorageService _blobStorageService;

    public DownloadExpeditionListHandler(IBlobStorageService blobStorageService)
    {
        _blobStorageService = blobStorageService;
    }

    public async Task<DownloadExpeditionListResponse> Handle(DownloadExpeditionListRequest request, CancellationToken cancellationToken)
    {
        if (!IsValidBlobPath(request.BlobPath))
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

    private static bool IsValidBlobPath(string blobPath)
    {
        if (string.IsNullOrWhiteSpace(blobPath))
            return false;

        if (blobPath.Contains(".."))
            return false;

        if (!ValidBlobPathPattern.IsMatch(blobPath))
            return false;

        var datePart = blobPath.Split('/')[0];
        return DateOnly.TryParseExact(datePart, "yyyy-MM-dd", out _);
    }
}
