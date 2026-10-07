namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

/// <summary><paramref name="CurrentValue"/> uses the <see cref="AdActionValues"/> vocabulary.</summary>
public sealed record AdTargetState(bool Exists, string? CurrentValue, string? RawJson);
