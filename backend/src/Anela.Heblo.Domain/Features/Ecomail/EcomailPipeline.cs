namespace Anela.Heblo.Domain.Features.Ecomail;

/// <summary>An Ecomail automation ("pipeline"). Id is Ecomail's own id.</summary>
public class EcomailPipeline
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? ListId { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime SyncedAt { get; set; }
}
