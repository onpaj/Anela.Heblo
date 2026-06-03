using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Xcc.Services.Dashboard;

namespace Anela.Heblo.Application.Features.Logistics.DashboardTiles;

[TileId("errorboxes")]
public class ErrorBoxesTile : TransportBoxBaseTile
{
    public override string Title => "Boxy v chybě";
    public override string Description => "Počet chybových boxů";

    protected override TransportBoxState[] FilterStates => new[]
    {
        TransportBoxState.Error
    };

    public ErrorBoxesTile(ITransportBoxRepository repository) : base(repository)
    {
    }
}