using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Xcc.Services.Dashboard;

namespace Anela.Heblo.Application.Features.Logistics.DashboardTiles;

[TileId("intransitboxes")]
public class InTransitBoxesTile : TransportBoxBaseTile
{
    public override string Title => "Boxy v přepravě";
    public override string Description => "Počet boxů v přepravě";

    protected override TransportBoxState[] FilterStates => new[]
    {
        TransportBoxState.InTransit
    };

    public InTransitBoxesTile(ITransportBoxRepository repository) : base(repository)
    {
    }
}
