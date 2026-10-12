namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

/// <summary>Where a change event came from: the platform's own change history, or Heblo diffing two snapshots.</summary>
public enum AdChangeSource
{
    PlatformChangeLog = 1,
    SnapshotDiff = 2,
}
