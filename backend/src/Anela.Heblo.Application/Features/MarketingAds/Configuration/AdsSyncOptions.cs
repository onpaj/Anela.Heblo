namespace Anela.Heblo.Application.Features.MarketingAds.Configuration;

/// <summary>
/// The "Ads" configuration section (shared with C3/C4, which bind their own classes to it).
/// Validated by <see cref="EnsureValid"/> at the start of every run — never in a job constructor
/// or with ValidateOnStart, because a throw there unschedules every recurring job or stops the app.
/// </summary>
public sealed class AdsSyncOptions
{
    public const string SectionName = "Ads";
    public const int MaxFactLookbackDays = 30;
    public const int MaxBatchSize = 5000;

    /// <summary>Days before yesterday re-pulled every day; conversions are attributed late.</summary>
    public int FactLookbackDays { get; set; } = 14;

    /// <summary>Rows per SaveChanges, keeping each statement well under the 10 s per-attempt DB timeout.</summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>"Platform:externalId" keys that are Anela's own accounts. Empty = every discovered account is managed.</summary>
    public string[] ManagedAccounts { get; set; } = Array.Empty<string>();

    public int OutOfBandMatchWindowMinutes { get; set; } = 120;

    public AdsDailySyncOptions DailySync { get; set; } = new();
    public AdsChangeSyncOptions ChangeSync { get; set; } = new();

    public IReadOnlySet<string> ManagedAccountKeys() =>
        ManagedAccounts.Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public void EnsureValid()
    {
        var problems = new List<string>();
        if (FactLookbackDays is < 0 or > MaxFactLookbackDays)
            problems.Add($"Ads:FactLookbackDays must be 0..{MaxFactLookbackDays}, was {FactLookbackDays}");
        if (BatchSize is < 1 or > MaxBatchSize)
            problems.Add($"Ads:BatchSize must be 1..{MaxBatchSize}, was {BatchSize}");
        if (OutOfBandMatchWindowMinutes < 1)
            problems.Add($"Ads:OutOfBandMatchWindowMinutes must be positive, was {OutOfBandMatchWindowMinutes}");
        if (ChangeSync.InitialLookbackDays < 1)
            problems.Add($"Ads:ChangeSync:InitialLookbackDays must be >= 1, was {ChangeSync.InitialLookbackDays}");
        if (problems.Count > 0)
            throw new InvalidOperationException("Invalid Ads configuration: " + string.Join("; ", problems));
    }
}
