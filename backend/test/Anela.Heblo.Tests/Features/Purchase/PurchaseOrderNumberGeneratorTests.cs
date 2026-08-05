using Anela.Heblo.Domain.Features.Purchase;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Purchase;

public class PurchaseOrderNumberGeneratorTests
{
    private readonly PurchaseOrderNumberGenerator _generator = new();

    [Fact]
    public void GenerateCandidate_FirstAttempt_UsesOrderDateForPrefixAndInstantForTimeSuffix()
    {
        var orderDate = new DateTime(2026, 8, 3);
        var now = new DateTimeOffset(2026, 8, 3, 14, 30, 22, 123, TimeSpan.Zero);

        var result = _generator.GenerateCandidate(orderDate, now, attempt: 1);

        result.Should().Be("PO20260803-143022123");
    }

    [Fact]
    public void GenerateCandidate_RetryAttempt_AppendsAttemptSuffix()
    {
        var orderDate = new DateTime(2026, 8, 3);
        var now = new DateTimeOffset(2026, 8, 3, 14, 30, 22, 123, TimeSpan.Zero);

        var result = _generator.GenerateCandidate(orderDate, now, attempt: 2);

        result.Should().Be("PO20260803-143022123-2");
    }

    [Fact]
    public void GenerateCandidate_DateAndTimeDeriveFromDifferentSources_StayConsistentAcrossMidnight()
    {
        // orderDate legitimately differs from "now" (e.g. backdated order); the time
        // suffix must still come entirely from the single `now` instant, never from a
        // separate local-time read that could disagree with it near a day boundary.
        var orderDate = new DateTime(2026, 8, 2);
        var now = new DateTimeOffset(2026, 8, 3, 0, 0, 5, 7, TimeSpan.Zero);

        var result = _generator.GenerateCandidate(orderDate, now, attempt: 1);

        result.Should().Be("PO20260802-000005007");
    }

    [Fact]
    public void GenerateCandidate_SameInstantDifferentAttempts_ProducesDistinctCandidates()
    {
        var orderDate = new DateTime(2026, 8, 3);
        var now = new DateTimeOffset(2026, 8, 3, 9, 15, 0, TimeSpan.Zero);

        var first = _generator.GenerateCandidate(orderDate, now, attempt: 1);
        var second = _generator.GenerateCandidate(orderDate, now, attempt: 2);
        var third = _generator.GenerateCandidate(orderDate, now, attempt: 3);

        first.Should().NotBe(second);
        second.Should().NotBe(third);
    }
}
