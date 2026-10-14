namespace Anela.Heblo.Application.Features.MarketingAds.Proposals;

/// <summary>
/// Bound to the "Ads" section (shared with C2's AdsSyncOptions; each class reads only its own properties).
/// Validated by <see cref="EnsureValid"/> at the start of every run that uses it — never with ValidateOnStart
/// or in a job constructor, because a throw there stops the app or unschedules every recurring job.
/// </summary>
public class AdProposalOptions
{
    public const string SectionName = "Ads";

    public int ProposalTtlHours { get; set; } = 72;
    public string ExpiryCronExpression { get; set; } = "*/15 * * * *";

    /// <summary>
    /// An Approved proposal not picked up by the execution job for this long is re-enqueued. Well above the
    /// 12–15 min queue latency seen with Hangfire's single worker, so a still-queued job is not duplicated.
    /// </summary>
    public int StuckApprovedRequeueMinutes { get; set; } = 60;

    public TimeSpan ProposalTtl => TimeSpan.FromHours(ProposalTtlHours);

    public void EnsureValid()
    {
        var problems = new List<string>();
        if (ProposalTtlHours < 1)
            problems.Add($"Ads:ProposalTtlHours must be positive, was {ProposalTtlHours}");
        if (StuckApprovedRequeueMinutes < 1)
            problems.Add($"Ads:StuckApprovedRequeueMinutes must be positive, was {StuckApprovedRequeueMinutes}");
        if (string.IsNullOrWhiteSpace(ExpiryCronExpression))
            problems.Add("Ads:ExpiryCronExpression must not be empty");
        if (problems.Count > 0)
            throw new InvalidOperationException("Invalid Ads proposal configuration: " + string.Join("; ", problems));
    }
}
