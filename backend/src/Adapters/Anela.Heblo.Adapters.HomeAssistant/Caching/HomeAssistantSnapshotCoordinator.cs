using Anela.Heblo.Domain.Features.Manufacture.Conditions;

namespace Anela.Heblo.Adapters.HomeAssistant.Caching;

public sealed class HomeAssistantSnapshotCoordinator
{
    public SemaphoreSlim Gate { get; } = new(initialCount: 1, maxCount: 1);

    public ConditionsSnapshot? LastObservedSnapshot { get; private set; }

    public ConditionsSnapshot? LastKnownGoodLive { get; private set; }

    public void RecordObserved(ConditionsSnapshot snapshot)
    {
        LastObservedSnapshot = snapshot;
    }

    public void RecordLive(ConditionsSnapshot snapshot)
    {
        LastObservedSnapshot = snapshot;
        LastKnownGoodLive = snapshot;
    }
}
