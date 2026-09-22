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
    public List<DateTimeOffset> ChangeQueriesRequested { get; } = new();
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

        var matching = Changes
            .Where(c => c.ChangeTime >= changedFrom)
            .Select(c => new ShoptetOrderCodeDto { Code = c.Code, ChangeTime = c.ChangeTime })
            .ToList();

        return Task.FromResult(Paginate(matching, page));
    }

    public Task<ShoptetOrderChangeListData> ListChangesAsync(
        DateTimeOffset changedFrom, int page, int itemsPerPage, CancellationToken ct = default)
    {
        if (page == 1)
            ChangeQueriesRequested.Add(changedFrom);

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
