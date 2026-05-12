namespace Anela.Heblo.Domain.Features.Smartsupp;

public class SmartsuppSyncState
{
    public int Id { get; set; }
    public DateTime LastSyncStartedAt { get; set; }
    public DateTime? LastUpdatedAtSeen { get; set; }
}
