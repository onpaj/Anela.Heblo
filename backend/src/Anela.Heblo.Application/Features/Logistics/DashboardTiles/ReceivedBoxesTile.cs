using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Xcc.Services.Dashboard;

namespace Anela.Heblo.Application.Features.Logistics.DashboardTiles;

[TileId("receivedboxes")]
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
