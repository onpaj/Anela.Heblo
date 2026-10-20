using NCrontab.Advanced;
using NCrontab.Advanced.Exceptions;

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
    public const string DefaultExpiryCronExpression = "*/15 * * * *";

    public string ExpiryCronExpression { get; set; } = DefaultExpiryCronExpression;
    public string ExpiryTimeZone { get; set; } = "Europe/Prague";

    /// <summary>
    /// An Approved proposal not picked up by the execution job for this long is re-enqueued. Well above the
    /// 12–15 min queue latency seen with Hangfire's single worker, so a still-queued job is not duplicated.
    /// </summary>
    public int StuckApprovedRequeueMinutes { get; set; } = 60;

    public TimeSpan ProposalTtl => TimeSpan.FromHours(ProposalTtlHours);

    /// <summary>Never throws: the configured cron when it parses, otherwise the default (job registration must not fail).</summary>
    public string ExpiryCronOrDefault => IsValidCron(ExpiryCronExpression) ? ExpiryCronExpression : DefaultExpiryCronExpression;

    public void EnsureValid()
    {
        var problems = new List<string>();
        if (ProposalTtlHours < 1)
            problems.Add($"Ads:ProposalTtlHours must be positive, was {ProposalTtlHours}");
        if (StuckApprovedRequeueMinutes < 1)
            problems.Add($"Ads:StuckApprovedRequeueMinutes must be positive, was {StuckApprovedRequeueMinutes}");
        if (!IsValidCron(ExpiryCronExpression))
            problems.Add($"Ads:ExpiryCronExpression must be a valid 5-field cron expression, was '{ExpiryCronExpression}'");
        if (problems.Count > 0)
            throw new InvalidOperationException("Invalid Ads proposal configuration: " + string.Join("; ", problems));
    }

    /// <summary>Same parser as the recurring-job cron editor (UpdateRecurringJobCronHandler).</summary>
    private static bool IsValidCron(string? cronExpression)
    {
        if (string.IsNullOrWhiteSpace(cronExpression))
            return false;

        try
        {
            CrontabSchedule.Parse(cronExpression);
            return true;
        }
        catch (CrontabException)
        {
            return false;
        }
    }
}
