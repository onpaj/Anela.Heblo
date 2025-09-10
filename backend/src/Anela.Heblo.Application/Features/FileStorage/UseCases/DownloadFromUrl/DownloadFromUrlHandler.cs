using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.FileStorage;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl;

public class DownloadFromUrlHandler : IRequestHandler<DownloadFromUrlRequest, DownloadFromUrlResponse>
{
    private readonly IBlobStorageService _blobStorageService;
    private readonly HttpClient _httpClient;
    private readonly ILogger<DownloadFromUrlHandler> _logger;

    public DownloadFromUrlHandler(
        IBlobStorageService blobStorageService,
        HttpClient httpClient,
        ILogger<DownloadFromUrlHandler> logger)
    {
        _blobStorageService = blobStorageService;
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<DownloadFromUrlResponse> Handle(DownloadFromUrlRequest request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Processing file download and upload request from URL: {FileUrl} to container: {ContainerName}", 
                request.FileUrl, request.ContainerName);

            // Validate URL format and scheme
            if (!Uri.TryCreate(request.FileUrl, UriKind.Absolute, out var uri) || 
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                _logger.LogWarning("Invalid URL format or unsupported scheme: {FileUrl}", request.FileUrl);
                return new DownloadFromUrlResponse
                {
                    Success = false,
                    ErrorCode = ErrorCodes.InvalidUrlFormat,
                    Params = new Dictionary<string, string> { { "fileUrl", request.FileUrl } }
                };
            }

            // Validate container name (must be lowercase and follow Azure naming rules)
            if (!IsValidContainerName(request.ContainerName))
            {
                _logger.LogWarning("Invalid container name: {ContainerName}", request.ContainerName);
                return new DownloadFromUrlResponse
                {
                    Success = false,
                    ErrorCode = ErrorCodes.InvalidContainerName,
                    Params = new Dictionary<string, string> { { "containerName", request.ContainerName } }
                };
            }

            // Check file size before downloading (if server supports HEAD requests)
            var fileSizeBytes = await GetFileSizeAsync(request.FileUrl, cancellationToken);

            // Download and upload the file
            var blobUrl = await _blobStorageService.DownloadFromUrlAsync(
                request.FileUrl, 
                request.ContainerName, 
                request.BlobName, 
                cancellationToken);

            // Extract blob name from URL if not provided in request
            var actualBlobName = request.BlobName ?? GetBlobNameFromUrl(blobUrl);

            _logger.LogInformation("Successfully processed file upload. Blob URL: {BlobUrl}", blobUrl);

            return new DownloadFromUrlResponse
            {
                Success = true,
                BlobUrl = blobUrl,
                BlobName = actualBlobName,
                ContainerName = request.ContainerName,
                FileSizeBytes = fileSizeBytes
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("File upload operation was cancelled for URL: {FileUrl}", request.FileUrl);
            throw; // Re-throw cancellation exceptions
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error downloading file from URL: {FileUrl}", request.FileUrl);
            return new DownloadFromUrlResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.FileDownloadFailed,
                Params = new Dictionary<string, string> 
                { 
                    { "fileUrl", request.FileUrl },
                    { "error", ex.Message }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing file upload request from URL: {FileUrl}", request.FileUrl);
            return new DownloadFromUrlResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.InternalServerError,
                Params = new Dictionary<string, string> 
                { 
                    { "fileUrl", request.FileUrl },
                    { "error", ex.Message }
                }
            };
        }
    }

    private static bool IsValidContainerName(string containerName)
    {
        // Azure container naming rules:
        // - Must be lowercase
        // - Can contain only lowercase letters, numbers, and hyphens
        // - Must be between 3 and 63 characters
        // - Must start with letter or number
        // - Cannot have consecutive hyphens
        
        if (string.IsNullOrEmpty(containerName) || containerName.Length < 3 || containerName.Length > 63)
            return false;

        if (containerName != containerName.ToLowerInvariant())
            return false;

        if (!char.IsLetterOrDigit(containerName[0]) || !char.IsLetterOrDigit(containerName[^1]))
            return false;

        for (int i = 0; i < containerName.Length; i++)
        {
            var c = containerName[i];
            if (!char.IsLetterOrDigit(c) && c != '-')
                return false;

            if (c == '-' && i < containerName.Length - 1 && containerName[i + 1] == '-')
                return false;
        }

        return true;
    }

    private async Task<long> GetFileSizeAsync(string fileUrl, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, fileUrl);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            
            if (response.IsSuccessStatusCode && response.Content.Headers.ContentLength.HasValue)
            {
                return response.Content.Headers.ContentLength.Value;
            }
        }
        catch (OperationCanceledException)
        {
            throw; // Re-throw cancellation exceptions
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not determine file size for URL: {FileUrl}", fileUrl);
        }

        return 0; // Unknown size
    }

    private static string GetBlobNameFromUrl(string blobUrl)
    {
        try
        {
            var uri = new Uri(blobUrl);
            return Path.GetFileName(uri.LocalPath);
        }
        catch
        {
            return "uploaded-file";
        }
    }
}