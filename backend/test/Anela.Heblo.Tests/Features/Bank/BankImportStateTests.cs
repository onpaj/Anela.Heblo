using Anela.Heblo.Domain.Features.Bank;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Bank;

public sealed class BankImportStateTests
{
    [Fact]
    public void Constructor_SetsAccountAndId()
    {
        var state = new BankImportState("ComgateCZK");

        state.Account.Should().Be("ComgateCZK");
        state.Id.Should().Be("ComgateCZK");
        state.LastValidImportDate.Should().BeNull();
        state.ConsecutiveFailureCount.Should().Be(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Constructor_Throws_WhenAccountMissing(string? account)
    {
        var act = () => new BankImportState(account!);
        act.Should().Throw<ArgumentException>().WithParameterName("account");
    }

    [Fact]
    public void RecordSuccess_AdvancesWatermark_ToDateOnly_AndClearsFailure()
    {
        var state = new BankImportState("ComgateCZK");
        state.RecordFailure("boom", new DateTime(2026, 6, 1), new DateTime(2026, 6, 1));

        var watermark = new DateTime(2026, 6, 14, 4, 30, 0);
        var start = new DateTime(2026, 6, 15, 4, 30, 0);
        var finish = new DateTime(2026, 6, 15, 4, 31, 0);
        state.RecordSuccess(watermark, start, finish);

        state.LastValidImportDate.Should().Be(new DateTime(2026, 6, 14)); // time stripped
        state.LastRunStatus.Should().Be("OK");
        state.LastErrorMessage.Should().BeNull();
        state.ConsecutiveFailureCount.Should().Be(0);
        state.LastRunStartedAt.Should().Be(start);
        state.LastRunFinishedAt.Should().Be(finish);
    }

    [Fact]
    public void RecordFailure_DoesNotAdvanceWatermark_AndIncrementsCount()
    {
        var state = new BankImportState("ComgateCZK");
        state.RecordSuccess(new DateTime(2026, 6, 10), DateTime.UtcNow, DateTime.UtcNow);

        state.RecordFailure("first", DateTime.UtcNow, DateTime.UtcNow);
        state.RecordFailure("second", DateTime.UtcNow, DateTime.UtcNow);

        state.LastValidImportDate.Should().Be(new DateTime(2026, 6, 10)); // unchanged
        state.LastRunStatus.Should().Be("ERROR");
        state.LastErrorMessage.Should().Be("second");
        state.ConsecutiveFailureCount.Should().Be(2);
    }
}
