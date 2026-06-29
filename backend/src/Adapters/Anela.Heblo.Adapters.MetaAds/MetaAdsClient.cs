using System.Net.Http.Json;
using System.Text.Json;
using Anela.Heblo.Domain.Features.Campaigns;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.MetaAds;

public class MetaAdsClient : IMetaAdsClient
{
    private readonly HttpClient _httpClient;
    private readonly MetaAdsSettings _settings;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public MetaAdsClient(HttpClient httpClient, IOptions<MetaAdsSettings> options)
    {
        _httpClient = httpClient;
        _settings = options.Value;
    }

    public async Task<IReadOnlyList<MetaCampaignDto>> GetCampaignsAsync(CancellationToken ct = default)
    {
        var url = $"/{_settings.ApiVersion}/act_{_settings.AccountId}/campaigns" +
                  "?fields=id,name,status,objective,daily_budget,lifetime_budget,start_time,stop_time" +
                  "&limit=100";

        var response = await _httpClient.GetFromJsonAsync<MetaListResponse<MetaCampaignResponse>>(url, JsonOptions, ct)
            ?? new MetaListResponse<MetaCampaignResponse>();

        return response.Data.Select(c => new MetaCampaignDto
        {
            Id = c.Id,
            Name = c.Name,
            Status = c.Status,
            Objective = c.Objective,
            DailyBudget = ParseDecimalCents(c.DailyBudget),
            LifetimeBudget = ParseDecimalCents(c.LifetimeBudget),
            StartTime = ParseDateTime(c.StartTime),
            StopTime = ParseDateTime(c.StopTime),
        }).ToList();
    }

    public async Task<IReadOnlyList<MetaAdSetDto>> GetAdSetsAsync(string campaignId, CancellationToken ct = default)
    {
        var url = $"/{_settings.ApiVersion}/{campaignId}/adsets" +
                  "?fields=id,campaign_id,name,status,daily_budget" +
                  "&limit=100";

        var response = await _httpClient.GetFromJsonAsync<MetaListResponse<MetaAdSetResponse>>(url, JsonOptions, ct)
            ?? new MetaListResponse<MetaAdSetResponse>();

        return response.Data.Select(s => new MetaAdSetDto
        {
            Id = s.Id,
            CampaignId = s.CampaignId,
            Name = s.Name,
            Status = s.Status,
            DailyBudget = ParseDecimalCents(s.DailyBudget),
        }).ToList();
    }

    public async Task<IReadOnlyList<MetaAdDto>> GetAdsAsync(string adSetId, CancellationToken ct = default)
    {
        var url = $"/{_settings.ApiVersion}/{adSetId}/ads" +
                  "?fields=id,adset_id,name,status" +
                  "&limit=100";

        var response = await _httpClient.GetFromJsonAsync<MetaListResponse<MetaAdResponse>>(url, JsonOptions, ct)
            ?? new MetaListResponse<MetaAdResponse>();

        return response.Data.Select(a => new MetaAdDto
        {
            Id = a.Id,
            AdSetId = a.AdSetId,
            Name = a.Name,
            Status = a.Status,
        }).ToList();
    }

    public async Task<IReadOnlyList<MetaInsightDto>> GetInsightsAsync(
        string adId,
        DateTime since,
        DateTime until,
        CancellationToken ct = default)
    {
        var sinceStr = since.ToString("yyyy-MM-dd");
        var untilStr = until.ToString("yyyy-MM-dd");

        var url = $"/{_settings.ApiVersion}/{adId}/insights" +
                  "?fields=ad_id,date_start,impressions,clicks,spend,action_values,actions" +
                  "&time_increment=1" +
                  $"&time_range={{\"since\":\"{sinceStr}\",\"until\":\"{untilStr}\"}}";

        var response = await _httpClient.GetFromJsonAsync<MetaListResponse<MetaInsightResponse>>(url, JsonOptions, ct)
            ?? new MetaListResponse<MetaInsightResponse>();

        return response.Data.Select(i => new MetaInsightDto
        {
            AdId = adId,
            Date = DateTime.Parse(i.DateStart, null, System.Globalization.DateTimeStyles.AssumeUniversal),
            Impressions = ParseLong(i.Impressions),
            Clicks = ParseLong(i.Clicks),
            Spend = ParseDecimal(i.Spend),
            Revenue = GetActionValue(i.ActionValues, "offsite_conversion.fb_pixel_purchase"),
            Conversions = GetActionCount(i.Actions, "offsite_conversion.fb_pixel_purchase"),
        }).ToList();
    }

    private static decimal? ParseDecimalCents(string? value)
    {
        if (string.IsNullOrEmpty(value) || !decimal.TryParse(value, out var result))
            return null;
        // Meta returns budget in cents
        return result / 100m;
    }

    private static decimal ParseDecimal(string value)
    {
        return decimal.TryParse(value, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var result) ? result : 0m;
    }

    private static long ParseLong(string value)
    {
        return long.TryParse(value, out var result) ? result : 0L;
    }

    private static DateTime? ParseDateTime(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.AssumeUniversal, out var dt)
            ? dt.ToUniversalTime()
            : null;
    }

    private static decimal GetActionValue(List<MetaActionValue>? actions, string actionType)
    {
        if (actions is null) return 0m;
        var match = actions.FirstOrDefault(a => a.ActionType == actionType);
        return match is null ? 0m : ParseDecimal(match.Value);
    }

    private static long GetActionCount(List<MetaActionValue>? actions, string actionType)
    {
        if (actions is null) return 0L;
        var match = actions.FirstOrDefault(a => a.ActionType == actionType);
        return match is null ? 0L : ParseLong(match.Value);
    }
}
