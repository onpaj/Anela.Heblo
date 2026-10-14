namespace Anela.Heblo.Application.Features.Ecomail.Services;

/// <summary>
/// Outcome of one sync run. Named Report, not Response: a reflection contract test fails CI for any
/// Application type ending in "Response" that does not inherit BaseResponse.
/// </summary>
/// <remarks>
/// <see cref="CampaignsUpserted"/> and <see cref="PipelinesUpserted"/> count metadata rows — they
/// increment as soon as a campaign or pipeline is listed, before any stats call, so they are useful
/// for the log line but must never be used as a health signal: a run where every stats call fails
/// still reports both as positive. <see cref="CampaignStatsFetched"/>, <see cref="SnapshotsWritten"/>
/// and <see cref="AutomationMonthsComputed"/> are the counters that represent real data landing.
/// </remarks>
public sealed record EcomailSyncReport(
    int CampaignsUpserted,
    int PipelinesUpserted,
    int SnapshotsWritten,
    int AutomationMonthsComputed,
    int CampaignStatsFetched,
    IReadOnlyList<string> Errors)
{
    public bool IsFullSuccess => Errors.Count == 0;
}
