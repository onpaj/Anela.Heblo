using Anela.Heblo.Adapters.HomeAssistant.Caching;
using Anela.Heblo.Domain.Features.Manufacture.Conditions;
using FluentAssertions;

namespace Anela.Heblo.Adapters.HomeAssistant.Tests;

public class HomeAssistantSnapshotCoordinatorTests
{
    private static ConditionsSnapshot Snap(ConditionsReadingSource source, DateTime? recordedAt = null) =>
        new(21m, 55m, 18m, 72m, recordedAt ?? DateTime.UtcNow, source);

    [Fact]
    public void RecordObserved_StoresAnySource()
    {
        var c = new HomeAssistantSnapshotCoordinator();
        var partial = Snap(ConditionsReadingSource.Partial);
        c.RecordObserved(partial);
        c.LastObservedSnapshot.Should().Be(partial);
    }

    [Fact]
    public void RecordLive_UpdatesBothObservedAndLastKnownGood()
    {
        var c = new HomeAssistantSnapshotCoordinator();
        var live = Snap(ConditionsReadingSource.Live);
        c.RecordLive(live);
        c.LastObservedSnapshot.Should().Be(live);
        c.LastKnownGoodLive.Should().Be(live);
    }

    [Fact]
    public void RecordObserved_WithPartial_DoesNotOverwriteLastKnownGoodLive()
    {
        var c = new HomeAssistantSnapshotCoordinator();
        var live = Snap(ConditionsReadingSource.Live);
        var partial = Snap(ConditionsReadingSource.Partial);
        c.RecordLive(live);
        c.RecordObserved(partial);
        c.LastKnownGoodLive.Should().Be(live);
        c.LastObservedSnapshot.Should().Be(partial);
    }

    [Fact]
    public void Gate_IsSingleFlight()
    {
        var c = new HomeAssistantSnapshotCoordinator();
        c.Gate.Wait();
        c.Gate.CurrentCount.Should().Be(0);
        c.Gate.Release();
        c.Gate.CurrentCount.Should().Be(1);
    }
}
