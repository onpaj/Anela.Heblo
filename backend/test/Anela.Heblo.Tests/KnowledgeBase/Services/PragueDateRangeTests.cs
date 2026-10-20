using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.Services;

public class PragueDateRangeTests
{
    [Fact]
    public void StartOfDay_SummerTime_IsPreviousDay22Utc()
    {
        PragueDateRange.StartOfDay(new DateOnly(2026, 10, 14))
            .Should().Be(new DateTimeOffset(2026, 10, 13, 22, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void StartOfDay_WinterTime_IsPreviousDay23Utc()
    {
        PragueDateRange.StartOfDay(new DateOnly(2026, 1, 15))
            .Should().Be(new DateTimeOffset(2026, 1, 14, 23, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void EndExclusive_IsStartOfNextPragueDay()
    {
        PragueDateRange.EndExclusive(new DateOnly(2026, 10, 14))
            .Should().Be(new DateTimeOffset(2026, 10, 14, 22, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void EndExclusive_AcrossDstSwitch_UsesNextDaysOffset()
    {
        // 2026-10-25 is the CEST -> CET switch day; the next day starts at 23:00 UTC.
        PragueDateRange.EndExclusive(new DateOnly(2026, 10, 25))
            .Should().Be(new DateTimeOffset(2026, 10, 25, 23, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Null_ReturnsNull()
    {
        PragueDateRange.StartOfDay(null).Should().BeNull();
        PragueDateRange.EndExclusive(null).Should().BeNull();
    }

    [Fact]
    public void EndExclusive_MaxValue_ReturnsNullInsteadOfThrowing()
    {
        PragueDateRange.EndExclusive(DateOnly.MaxValue).Should().BeNull();
    }
}
