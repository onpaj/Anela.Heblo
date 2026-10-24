using System.Collections.Concurrent;
using System.Diagnostics;
using Anela.Heblo.Domain.Accounting.Ledger;
using AutoMapper;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rem.FlexiBeeSDK.Client.Clients.Accounting.Ledger;
using Rem.FlexiBeeSDK.Model.Accounting.Ledger;

namespace Anela.Heblo.Adapters.Flexi.Accounting.Ledger;

public class LedgerService : ILedgerService
{
    private readonly ILedgerClient _ledgerClient;
    private readonly IMemoryCache _cache;
    private readonly IMapper _mapper;
    private readonly ILogger<LedgerService> _logger;
    private readonly FlexiLedgerOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, Lazy<Task<IList<LedgerItem>>>> _inflight = new();

    public LedgerService(
        ILedgerClient ledgerClient,
        IMemoryCache cache,
        IMapper mapper,
        ILogger<LedgerService> logger,
        IOptions<FlexiLedgerOptions> options,
        TimeProvider timeProvider)
    {
        _ledgerClient = ledgerClient ?? throw new ArgumentNullException(nameof(ledgerClient));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<IList<LedgerItem>> GetLedgerItems(DateTime dateFrom, DateTime dateTo, IEnumerable<string>? debitAccountPrefix = null, IEnumerable<string>? creditAccountPrefix = null, string? department = null, CancellationToken cancellationToken = default)
    {
        var debitPrefixes = debitAccountPrefix?.ToList() ?? new List<string>();
        var creditPrefixes = creditAccountPrefix?.ToList() ?? new List<string>();

        var cacheKey = $"ledger_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}_{string.Join(",", debitPrefixes)}_{string.Join(",", creditPrefixes)}_{department}";

        if (_cache.TryGetValue(cacheKey, out IList<LedgerItem>? cachedResult))
        {
            return cachedResult!;
        }

        // Single-flight: concurrent callers for the same key share one FlexiBee request.
        var lazy = _inflight.GetOrAdd(
            cacheKey,
            _ => new Lazy<Task<IList<LedgerItem>>>(
                () => FetchAsync(cacheKey, dateFrom, dateTo, debitPrefixes, creditPrefixes, department),
                LazyThreadSafetyMode.ExecutionAndPublication));

        var stopwatch = Stopwatch.StartNew();
        try
        {
            return await lazy.Value.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "FlexiBee ucetni-denik/query request was canceled by the caller (client abort). " +
                "DateFrom: {DateFrom}, DateTo: {DateTo}, ElapsedMs: {ElapsedMs}",
                dateFrom, dateTo, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    private async Task<IList<LedgerItem>> FetchAsync(
        string cacheKey,
        DateTime dateFrom,
        DateTime dateTo,
        List<string> debitPrefixes,
        List<string> creditPrefixes,
        string? department)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            // The shared request must not be tied to any single caller's token.
            var flexiData = await _ledgerClient.GetAsync(
                dateFrom,
                dateTo,
                debitPrefixes,
                creditPrefixes,
                department,
                cancellationToken: CancellationToken.None);

            IList<LedgerItem> result = _mapper.Map<List<LedgerItem>>(flexiData);

            var ttl = GetCacheTtl(dateTo);
            _cache.Set(cacheKey, result, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl });

            _logger.LogInformation(
                "FlexiLedger.QueryCompleted from={From} to={To} spanDays={SpanDays} debit={Debit} credit={Credit} dept={Dept} rows={Rows} elapsedMs={ElapsedMs}",
                dateFrom, dateTo, (dateTo.Date - dateFrom.Date).TotalDays,
                string.Join(",", debitPrefixes), string.Join(",", creditPrefixes), department,
                result.Count, stopwatch.ElapsedMilliseconds);

            return result;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex,
                "FlexiBee ucetni-denik/query request timed out (internal HttpClient timeout). " +
                "DateFrom: {DateFrom}, DateTo: {DateTo}, ElapsedMs: {ElapsedMs}",
                dateFrom, dateTo, stopwatch.ElapsedMilliseconds);
            throw;
        }
        finally
        {
            _inflight.TryRemove(cacheKey, out _);
        }
    }

    private TimeSpan GetCacheTtl(DateTime dateTo)
    {
        var now = _timeProvider.GetLocalNow();
        var firstOfCurrentMonth = new DateTime(now.Year, now.Month, 1);
        var minutes = dateTo.Date < firstOfCurrentMonth
            ? _options.ClosedPeriodCacheMinutes
            : _options.OpenPeriodCacheMinutes;
        return TimeSpan.FromMinutes(minutes);
    }

    public Task<IList<CostStatistics>> GetPersonalCosts(DateTime dateFrom, DateTime dateTo, string? department = null, CancellationToken cancellationToken = default) =>
        GetCosts(dateFrom, dateTo, ["52"], department, cancellationToken);

    public Task<IList<CostStatistics>> GetDirectCosts(DateTime dateFrom, DateTime dateTo, string? department = null, CancellationToken cancellationToken = default) =>
        GetCosts(dateFrom, dateTo, ["51", "52"], department, cancellationToken);

    public async Task<IList<CostStatistics>> GetCosts(DateTime dateFrom, DateTime dateTo, IEnumerable<string> debitAccountPrefixes, string? department = null, CancellationToken cancellationToken = default)
    {
        // Přímé náklady na účtech začínajících 50, 51, 52 (náklady na prodané zboží, služby, osobní náklady)
        var ledgerItems = await GetLedgerItems(dateFrom, dateTo, debitAccountPrefixes, null, department, cancellationToken);

        // Seskupení podle data a sečtení nákladů pro každý den
        var dailyCosts = ledgerItems
            .GroupBy(item => new
            {
                Date = item.Date.Date, // Pouze datum bez času
                Department = item.Department
            })
            .Select(g => new CostStatistics
            {
                Date = g.Key.Date,
                Department = g.Key.Department,
                Cost = g.Sum(item => item.Amount)
            })
            .OrderBy(cs => cs.Date)
            .ToList();

        return dailyCosts;
    }
}