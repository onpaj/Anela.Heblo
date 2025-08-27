namespace Anela.Heblo.Domain.Features.Logistics.Transport;

public enum TransportBoxState
{
    New,
    Opened,
    InTransit,
    Received,
    Stocked,
    Closed,
    Error,

    Reserve
}