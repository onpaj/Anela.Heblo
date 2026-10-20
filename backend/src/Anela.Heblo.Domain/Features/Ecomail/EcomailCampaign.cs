namespace Anela.Heblo.Domain.Features.Ecomail;

/// <summary>
/// One Ecomail campaign. Id is Ecomail's own campaign id (natural key, not generated).
/// Stats are lifetime totals, which for a one-off send are final within days — so they live
/// here as columns rather than in a snapshot table. Counts only; rates are derived at read time.
/// </summary>
public class EcomailCampaign
{
    /// <summary>Campaign types that represent a real newsletter send. See CLUSTER-B-FINDINGS.md §8.6.</summary>
    public static readonly string[] ReportableTypes = { "email", "ab" };

    /// <summary>Ecomail status code 3 = sent.</summary>
    public const int SentStatus = 3;

    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string? FromEmail { get; set; }

    /// <summary>"email" | "ab" | "variation" | "sms". String, not enum: Ecomail may add more.</summary>
    public string CampaignType { get; set; } = string.Empty;

    public int Status { get; set; }

    /// <summary>Set on every sent non-variation campaign, without exception across 250 campaigns.</summary>
    public DateTime? SentAt { get; set; }

    /// <summary>Set on "variation" rows, pointing at the owning "ab" campaign.</summary>
    public int? ParentId { get; set; }

    public int Recipients { get; set; }

    public int Inject { get; set; }
    public int Delivery { get; set; }
    public int Open { get; set; }
    public int TotalOpen { get; set; }
    public int Click { get; set; }
    public int TotalClick { get; set; }
    public int Unsub { get; set; }
    public int Bounce { get; set; }
    public int Spam { get; set; }
    public int Conversions { get; set; }
    public decimal ConversionsValue { get; set; }

    public DateTime SyncedAt { get; set; }

    /// <summary>
    /// True when this row is a newsletter that should appear in reporting: a sent campaign that is
    /// neither an A/B arm (its numbers are a subset of its parent's) nor SMS.
    /// </summary>
    public bool IsReportable =>
        Status == SentStatus && ReportableTypes.Contains(CampaignType);
}
