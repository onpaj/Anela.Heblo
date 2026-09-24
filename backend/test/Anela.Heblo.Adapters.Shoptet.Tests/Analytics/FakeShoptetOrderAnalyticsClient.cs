using Anela.Heblo.Adapters.ShoptetApi.Analytics;
using Anela.Heblo.Adapters.ShoptetApi.Analytics.Model;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Analytics;

/// <summary>
/// In-memory stand-in for the Shoptet API. Hand-written rather than mocked because the sync
/// services care about paging behaviour, which is tedious to express with Moq setups.
/// </summary>
internal sealed class FakeShoptetOrderAnalyticsClient : IShoptetOrderAnalyticsClient
{
    private const int PageSize = 50;

    public List<(DateTimeOffset From, DateTimeOffset To)> CreationWindowsRequested { get; } = new();
    /// <summary>GET /api/orders?changeTimeFrom — the listing.</summary>
    public List<DateTimeOffset> ChangeQueriesRequested { get; } = new();

    /// <summary>GET /api/orders/changes — the edit/delete log. Recorded separately so a test can
    /// tell which of the two sources the incremental sync actually read.</summary>
    public List<DateTimeOffset> ChangeLogQueriesRequested { get; } = new();
    public List<string> DetailsRequested { get; } = new();

    /// <summary>Order code → creation instant, used to answer the creation-time listing.</summary>
    public Dictionary<string, DateTimeOffset> OrdersByCode { get; } = new();

    /// <summary>Order code → detail JSON. A code missing from here answers 404.</summary>
    public Dictionary<string, string> DetailJsonByCode { get; } = new();

    public List<ShoptetOrderChangeDto> Changes { get; } = new();

    /// <summary>
    /// Invoked once per creation-time window. Lets a test advance a FakeTimeProvider so the
    /// backfill's wall-clock budget can actually be reached.
    /// </summary>
    public Action? OnCreationWindow { get; set; }

    /// <summary>Invoked before each detail call, so a test can cancel a run already in flight.</summary>
    public Action? OnDetail { get; set; }

    public Task<ShoptetOrderCodeListData> ListCodesByCreationTimeAsync(
        DateTimeOffset createdFrom, DateTimeOffset createdTo, int page, CancellationToken ct = default)
    {
        if (page == 1)
        {
            CreationWindowsRequested.Add((createdFrom, createdTo));
            OnCreationWindow?.Invoke();
        }

        var matching = OrdersByCode
            .Where(kv => kv.Value >= createdFrom && kv.Value < createdTo)
            .OrderBy(kv => kv.Value)
            .Select(kv => new ShoptetOrderCodeDto { Code = kv.Key, CreationTime = kv.Value })
            .ToList();

        return Task.FromResult(Paginate(matching, page));
    }

    public Task<ShoptetOrderCodeListData> ListCodesByChangeTimeAsync(
        DateTimeOffset changedFrom, int page, CancellationToken ct = default)
    {
        if (page == 1)
            ChangeQueriesRequested.Add(changedFrom);

        // A deleted order stops appearing in the listing altogether — that is exactly why the
        // change log is the only way to learn about a deletion.
        var matching = Changes
            .Where(c => c.ChangeTime >= changedFrom)
            .Where(c => !string.Equals(c.ChangeType, "delete", StringComparison.OrdinalIgnoreCase))
            .Select(c => new ShoptetOrderCodeDto { Code = c.Code, ChangeTime = c.ChangeTime })
            .ToList();

        return Task.FromResult(Paginate(matching, page));
    }

    public Task<ShoptetOrderChangeListData> ListChangesAsync(
        DateTimeOffset changedFrom, int page, int itemsPerPage, CancellationToken ct = default)
    {
        if (page == 1)
            ChangeLogQueriesRequested.Add(changedFrom);

        var matching = Changes.Where(c => c.ChangeTime >= changedFrom).ToList();

        return Task.FromResult(new ShoptetOrderChangeListData
        {
            Changes = matching,
            Paginator = new ShoptetPaginatorDto
            {
                TotalCount = matching.Count,
                Page = page,
                PageCount = 1,
                ItemsOnPage = matching.Count,
                ItemsPerPage = itemsPerPage,
            },
        });
    }

    public async Task<ShoptetOrderDetailDto?> GetOrderAsync(string code, CancellationToken ct = default)
        => (await GetOrderWithRawAsync(code, ct)).Order;

    public Task<(ShoptetOrderDetailDto? Order, string RawJson)> GetOrderWithRawAsync(
        string code, CancellationToken ct = default)
    {
        DetailsRequested.Add(code);
        OnDetail?.Invoke();

        if (!DetailJsonByCode.TryGetValue(code, out var json))
            return Task.FromResult<(ShoptetOrderDetailDto?, string)>((null, "{}"));

        return Task.FromResult<(ShoptetOrderDetailDto?, string)>(
            (ShoptetOrderTestData.Parse(json), json));
    }

    public void AddOrder(string code, DateTimeOffset creationTime, string detailJson)
    {
        OrdersByCode[code] = creationTime;
        DetailJsonByCode[code] = detailJson.Replace("\"code\": \"126020373\"", $"\"code\": \"{code}\"")
                                           .Replace("\"code\": \"126014786\"", $"\"code\": \"{code}\"")
                                           .Replace("\"code\": \"126020422\"", $"\"code\": \"{code}\"");
    }

    private static ShoptetOrderCodeListData Paginate(List<ShoptetOrderCodeDto> all, int page)
    {
        var pageCount = all.Count == 0 ? 0 : (int)Math.Ceiling(all.Count / (double)PageSize);
        return new ShoptetOrderCodeListData
        {
            Orders = all.Skip((page - 1) * PageSize).Take(PageSize).ToList(),
            Paginator = new ShoptetPaginatorDto
            {
                TotalCount = all.Count,
                Page = page,
                PageCount = pageCount,
                ItemsOnPage = Math.Min(PageSize, Math.Max(0, all.Count - (page - 1) * PageSize)),
                ItemsPerPage = PageSize,
            },
        };
    }
}
