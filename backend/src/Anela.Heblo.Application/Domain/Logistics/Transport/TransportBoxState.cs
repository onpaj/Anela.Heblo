namespace Anela.Heblo.Application.Domain.Logistics.Transport;

public enum TransportBoxState
{
    New,
    Opened,
    InTransit,
    Received,
    InSwap,
    Stocked,
    Closed,
    Error,
    
    Reserve
}