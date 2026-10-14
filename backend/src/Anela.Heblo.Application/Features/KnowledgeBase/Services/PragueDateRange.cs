namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

/// <summary>
/// Converts inclusive Prague calendar days from the UI into a half-open UTC range
/// [StartOfDay(from), EndExclusive(to)). DST-aware: each bound uses the offset of its own local midnight.
/// </summary>
public static class PragueDateRange
{
    private static readonly TimeZoneInfo Prague = TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");

    public static DateTimeOffset? StartOfDay(DateOnly? day) =>
        day is null ? null : LocalMidnightUtc(day.Value);

    public static DateTimeOffset? EndExclusive(DateOnly? day) =>
        day is null ? null : LocalMidnightUtc(day.Value.AddDays(1));

    private static DateTimeOffset LocalMidnightUtc(DateOnly day)
    {
        var localMidnight = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(localMidnight, Prague.GetUtcOffset(localMidnight)).ToUniversalTime();
    }
}
