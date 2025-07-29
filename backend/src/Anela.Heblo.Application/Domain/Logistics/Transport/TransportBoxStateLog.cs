using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Application.Domain.Logistics.Transport;

public class TransportBoxStateLog : Entity<int>
{
    public TransportBoxState State { get; private set; }
    public DateTime StateDate { get; private set;}
    public string? User { get; private set;}
    public string? Description { get; set; }

    internal TransportBoxStateLog(TransportBoxState state, DateTime stateDate, string? user, string? description = null)
    {
        State = state;
        StateDate = stateDate;
        User = user;
        Description = description;
    }
}