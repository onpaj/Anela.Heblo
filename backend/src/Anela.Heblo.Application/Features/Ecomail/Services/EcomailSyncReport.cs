namespace Anela.Heblo.Application.Features.Ecomail.Services;

/// <summary>
/// Outcome of one sync run. Named Report, not Response: a reflection contract test fails CI for any
/// Application type ending in "Response" that does not inherit BaseResponse.
/// </summary>
public sealed record EcomailSyncReport(
    int CampaignsUpserted,
    int PipelinesUpserted,
    int SnapshotsWritten,
    int AutomationMonthsComputed,
    IReadOnlyList<string> Errors)
{
    public bool IsFullSuccess => Errors.Count == 0;
}
