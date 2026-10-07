namespace Anela.Heblo.Persistence.Ads.Entities;

/// <summary>
/// Search-term metrics (Google, Sklik), PK (ad_group_entity_id, date, search_term, match_type).
/// <see cref="MatchType"/> is a KeywordMatchType name, or <see cref="UnknownMatchType"/> when the
/// platform reports none — a primary-key column cannot be null.
/// </summary>
public class AdSearchTermDaily
{
    public const string UnknownMatchType = "Unknown";

    public long AdGroupEntityId { get; set; }
    public DateOnly Date { get; set; }
    public string SearchTerm { get; set; } = "";
    public string MatchType { get; set; } = UnknownMatchType;
    public long Impressions { get; set; }
    public long Clicks { get; set; }
    public decimal Cost { get; set; }
    public decimal Conversions { get; set; }
    public decimal ConversionValue { get; set; }
    public string Currency { get; set; } = "";
    public DateTimeOffset SyncedAt { get; set; }
}
