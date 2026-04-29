using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.FileStorage.Infrastructure;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Configuration;
using Anela.Heblo.Domain.Features.FileStorage;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl;

public sealed class DownloadFromUrlHandler : IRequestHandler<DownloadFromUrlRequest, DownloadFromUrlResponse>
{
    private readonly IBlobStorageService _blobStorageService;
    private readonly IDownloadResilienceService _resilience;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<ProductExportOptions> _options;
    private readonly ILogger<DownloadFromUrlHandler> _logger;

    public DownloadFromUrlHandler(
        IBlobStorageService blobStorageService,
        IDownloadResilienceService resilience,
        IHttpClientFactory httpClientFactory,
        IOptions<ProductExportOptions> options,
        ILogger<DownloadFromUrlHandler> logger)
    {
        _blobStorageService = blobStorageService;
        _resilience = resilience;
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public async Task<DownloadFromUrlResponse> Handle(DownloadFromUrlRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Processing file download and upload request from URL: {FileUrl} to container: {ContainerName}",
            request.FileUrl,
            request.ContainerName);

        if (!Uri.TryCreate(request.FileUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            _logger.LogWarning("Invalid URL format or unsupported scheme: {FileUrl}", request.FileUrl);
            return new DownloadFromUrlResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.InvalidUrlFormat,
                Params = new Dictionary<string, string>
                {
                    ["fileUrl"] = request.FileUrl,
                    ["cause"] = "validation",
                },
            };
        }

        if (!IsValidContainerName(request.ContainerName))
        {
            _logger.LogWarning("Invalid container name: {ContainerName}", request.ContainerName);
            return new DownloadFromUrlResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.InvalidContainerName,
                Params = new Dictionary<string, string>
                {
                    ["containerName"] = request.ContainerName,
                    ["cause"] = "validation",
                },
            };
        }

        var redactedUrl = RedactUrl(request.FileUrl);
        var sw = Stopwatch.StartNew();
        int attemptCount = 0;

        // HEAD probe — best-effort; never cancels the parent download
        var fileSizeBytes = await ProbeContentLengthAsync(request.FileUrl, cancellationToken);

        try
        {
            var blobUrl = await _resilience.ExecuteWithResilienceAsync(
                async ct =>
                {
                    Interlocked.Increment(ref attemptCount);
                    return await _blobStorageService.DownloadFromUrlAsync(
                        request.FileUrl,
                        request.ContainerName,
                        request.BlobName,
                        ct);
                },
                FileStorageModule.ProductExportDownloadClientName,
                cancellationToken);

            sw.Stop();
            var actualBlobName = request.BlobName ?? GetBlobNameFromUrl(blobUrl);

            _logger.LogInformation("Successfully processed file upload. Blob URL: {BlobUrl}", blobUrl);

            return new DownloadFromUrlResponse
            {
                Success = true,
                BlobUrl = blobUrl,
                BlobName = actualBlobName,
                ContainerName = request.ContainerName,
                FileSizeBytes = fileSizeBytes,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("File download operation was cancelled for URL: {FileUrl}", request.FileUrl);
            throw;
        }
        catch (OperationCanceledException oce)
        {
            sw.Stop();
            _logger.LogError(oce, "Download timed out for URL: {RedactedUrl}", redactedUrl);
            return Failure(redactedUrl, "timeout", attemptCount, sw.ElapsedMilliseconds, oce.Message);
        }
        catch (HttpRequestException hre)
        {
            sw.Stop();
            var cause = attemptCount > 1 ? "retry-exhausted" : "http-status";
            _logger.LogError(hre, "HTTP error downloading from URL: {RedactedUrl}", redactedUrl);
            return Failure(redactedUrl, cause, attemptCount, sw.ElapsedMilliseconds, hre.Message);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Unexpected failure during ProductExportDownload for URL: {RedactedUrl}", redactedUrl);
            return Failure(redactedUrl, "retry-exhausted", attemptCount, sw.ElapsedMilliseconds, ex.Message);
        }
    }

    private async Task<long> ProbeContentLengthAsync(string fileUrl, CancellationToken callerCt)
    {
        using var headCts = CancellationTokenSource.CreateLinkedTokenSource(callerCt);
        headCts.CancelAfter(_options.Value.HeadTimeout);
        try
        {
            var client = _httpClientFactory.CreateClient(FileStorageModule.ProductExportDownloadClientName);
            using var req = new HttpRequestMessage(HttpMethod.Head, fileUrl);
            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, headCts.Token);
            if (resp.IsSuccessStatusCode && resp.Content.Headers.ContentLength.HasValue)
                return resp.Content.Headers.ContentLength.Value;
        }
        catch (OperationCanceledException) when (callerCt.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("HEAD probe timed out for ProductExportDownload");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "HEAD probe failed for ProductExportDownload");
        }

        return 0L;
    }

    private static DownloadFromUrlResponse Failure(
        string redactedUrl,
        string cause,
        int attemptCount,
        long elapsedMs,
        string error) =>
        new()
        {
            Success = false,
            ErrorCode = ErrorCodes.FileDownloadFailed,
            Params = new Dictionary<string, string>
            {
                ["fileUrl"] = redactedUrl,
                ["cause"] = cause,
                ["attemptCount"] = attemptCount.ToString(),
                ["elapsedMs"] = elapsedMs.ToString(),
                ["error"] = error,
            },
        };

    private static string RedactUrl(string url)
    {
        try
        {
            var ub = new UriBuilder(url) { Query = null };
            return ub.Uri.ToString();
        }
        catch
        {
            return "[redacted]";
        }
    }

    private static bool IsValidContainerName(string containerName)
    {
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

    private static string GetBlobNameFromUrl(string blobUrl)
    {
        try
        {
            return Path.GetFileName(new Uri(blobUrl).LocalPath);
        }
        catch
        {
            return "uploaded-file";
        }
    }
}
