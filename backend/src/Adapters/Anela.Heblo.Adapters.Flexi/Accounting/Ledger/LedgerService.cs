using Anela.Heblo.Domain.Accounting.Ledger;
using AutoMapper;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Rem.FlexiBeeSDK.Client.Clients.Accounting.Ledger;
using Rem.FlexiBeeSDK.Model.Accounting.Ledger;

namespace Anela.Heblo.Adapters.Flexi.Accounting.Ledger;

public class LedgerService : ILedgerService
{
    private readonly ILedgerClient _ledgerClient;
    private readonly IMemoryCache _cache;
    private readonly IMapper _mapper;
    private readonly ILogger<LedgerService> _logger;

    public LedgerService(ILedgerClient ledgerClient, IMemoryCache cache, IMapper mapper, ILogger<LedgerService> logger)
    {
        _ledgerClient = ledgerClient ?? throw new ArgumentNullException(nameof(ledgerClient));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

        IReadOnlyList<LedgerItemFlexiDto> flexiData;
        try
        {
            // Získání dat z FlexiBee pomocí ILedgerClient s filtry
            flexiData = await _ledgerClient.GetAsync(
                dateFrom,
                dateTo,
                debitPrefixes,
                creditPrefixes,
                department,
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "FlexiBee ucetni-denik/query request timed out (internal HttpClient timeout). " +
                "DateFrom: {DateFrom}, DateTo: {DateTo}",
                dateFrom, dateTo);
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "FlexiBee ucetni-denik/query request was canceled by the caller (client abort). " +
                "DateFrom: {DateFrom}, DateTo: {DateTo}",
                dateFrom, dateTo);
            throw;
        }

        var result = _mapper.Map<List<LedgerItem>>(flexiData);

        // Cache na 15 minut
        var cacheOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15)
        };
        _cache.Set(cacheKey, result, cacheOptions);

        return result;
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