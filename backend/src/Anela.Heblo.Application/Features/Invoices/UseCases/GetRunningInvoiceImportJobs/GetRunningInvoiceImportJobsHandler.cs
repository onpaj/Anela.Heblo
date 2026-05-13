using Anela.Heblo.Xcc;
using Anela.Heblo.Xcc.Services;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Invoices.UseCases.GetRunningInvoiceImportJobs;

public class GetRunningInvoiceImportJobsHandler
    : IRequestHandler<GetRunningInvoiceImportJobsRequest, IList<BackgroundJobInfo>>
{
    internal const string CacheKey = "invoices:running-import-jobs";

    private readonly IBackgroundWorker _backgroundWorker;
    private readonly IMemoryCache _memoryCache;
    private readonly HangfireOptions _options;
    private readonly ILogger<GetRunningInvoiceImportJobsHandler> _logger;

    public GetRunningInvoiceImportJobsHandler(
        IBackgroundWorker backgroundWorker,
        IMemoryCache memoryCache,
        IOptions<HangfireOptions> options,
        ILogger<GetRunningInvoiceImportJobsHandler> logger)
    {
        _backgroundWorker = backgroundWorker;
        _memoryCache = memoryCache;
        _options = options.Value;
        _logger = logger;
    }

    public Task<IList<BackgroundJobInfo>> Handle(
        GetRunningInvoiceImportJobsRequest request,
        CancellationToken cancellationToken)
    {
        var cacheTtlSeconds = _options.RunningJobsCacheSeconds;

        if (cacheTtlSeconds > 0 &&
            _memoryCache.TryGetValue<IList<BackgroundJobInfo>>(CacheKey, out var cached) &&
            cached is not null)
        {
            return Task.FromResult(cached);
        }

        try
        {
            var runningJobs = _backgroundWorker.GetRunningJobs();
            var pendingJobs = _backgroundWorker.GetPendingJobs();

            // Filter for invoice import jobs based on job name containing "InvoiceImport"
            var invoiceImportJobs = runningJobs
                .Concat(pendingJobs)
                .Where(job => job.JobName != null &&
                              job.JobName.Contains("InvoiceImport", StringComparison.OrdinalIgnoreCase))
                .ToList();

            _logger.LogDebug("Found {Count} running/pending invoice import jobs", invoiceImportJobs.Count);

            IList<BackgroundJobInfo> result = invoiceImportJobs.AsReadOnly();

            if (cacheTtlSeconds > 0)
            {
                _memoryCache.Set(CacheKey, result, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(cacheTtlSeconds)
                });
            }

            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get running invoice import jobs");
            return Task.FromResult<IList<BackgroundJobInfo>>(new List<BackgroundJobInfo>());
        }
    }
}
