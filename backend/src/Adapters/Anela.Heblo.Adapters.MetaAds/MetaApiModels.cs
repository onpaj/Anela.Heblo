using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.MetaAds;

internal class MetaListResponse<T>
{
    [JsonPropertyName("data")]
    public List<T> Data { get; set; } = new();
}

internal class MetaCampaignResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("objective")]
    public string? Objective { get; set; }

    [JsonPropertyName("daily_budget")]
    public string? DailyBudget { get; set; }

    [JsonPropertyName("lifetime_budget")]
    public string? LifetimeBudget { get; set; }

    [JsonPropertyName("start_time")]
    public string? StartTime { get; set; }

    [JsonPropertyName("stop_time")]
    public string? StopTime { get; set; }
}

internal class MetaAdSetResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("campaign_id")]
    public string CampaignId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("daily_budget")]
    public string? DailyBudget { get; set; }
}

internal class MetaAdResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("adset_id")]
    public string AdSetId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}

internal class MetaInsightResponse
{
    [JsonPropertyName("ad_id")]
    public string AdId { get; set; } = string.Empty;

    [JsonPropertyName("date_start")]
    public string DateStart { get; set; } = string.Empty;

    [JsonPropertyName("impressions")]
    public string Impressions { get; set; } = "0";

    [JsonPropertyName("clicks")]
    public string Clicks { get; set; } = "0";

    [JsonPropertyName("spend")]
    public string Spend { get; set; } = "0";

    [JsonPropertyName("action_values")]
    public List<MetaActionValue>? ActionValues { get; set; }

    [JsonPropertyName("actions")]
    public List<MetaActionValue>? Actions { get; set; }
}

internal class MetaActionValue
{
    [JsonPropertyName("action_type")]
    public string ActionType { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = "0";
}
