using Anela.Heblo.Domain.Features.Logistics.Transport;

namespace Anela.Heblo.Application.Features.Logistics.Transport.Contracts;

public class TransportBoxDto
{
    public int Id { get; set; }
    public string? Code { get; set; }
    public string State { get; set; } = string.Empty;
    public string DefaultReceiveState { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime? LastStateChanged { get; set; }
    public string? Location { get; set; }
    public bool IsInTransit { get; set; }
    public bool IsInReserve { get; set; }
    public int ItemCount { get; set; }
    // Audit fields
    public DateTime CreationTime { get; set; }
    public Guid? CreatorId { get; set; }
    public DateTime? LastModificationTime { get; set; }
    public Guid? LastModifierId { get; set; }
    
    public IList<TransportBoxItemDto> Items { get; set; } = new List<TransportBoxItemDto>();
    public IList<TransportBoxStateLogDto> StateLog { get; set; } = new List<TransportBoxStateLogDto>();
    public IList<TransportBoxTransitionDto> AllowedTransitions { get; set; } = new List<TransportBoxTransitionDto>();
}