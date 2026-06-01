using Anela.Heblo.Domain.Features.Logistics.Transport;

namespace Anela.Heblo.Application.Features.Logistics.DashboardTiles;

public class ReceivedBoxesTile : TransportBoxBaseTile
{
    public override string Title => "Boxy přijaté";
    public override string Description => "Počet přijatých boxů";

    protected override TransportBoxState[] FilterStates => new[]
    {
        TransportBoxState.Received
    };

    public ReceivedBoxesTile(ITransportBoxRepository repository) : base(repository)
    {
    }
}
