using Anela.Heblo.Domain.Features.Logistics.Transport;

namespace Anela.Heblo.Application.Features.Transport.Dashboard;

public class StockedBoxesTile : TransportBoxBaseTile
{
    public override string Title => "Boxy naskladněné";
    public override string Description => "Počet naskladněných boxů";

    protected override TransportBoxState[] FilterStates => new[]
    {
        TransportBoxState.Stocked
    };

    public StockedBoxesTile(ITransportBoxRepository repository) : base(repository)
    {
    }
}
