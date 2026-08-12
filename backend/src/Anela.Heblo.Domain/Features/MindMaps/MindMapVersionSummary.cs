namespace Anela.Heblo.Domain.Features.MindMaps;

/// <summary>
/// Metadata-only projection of a <see cref="MindMapVersion"/> — everything the detail
/// read needs except the full <see cref="MindMapVersion.Json"/> blob, which the polling
/// detail endpoint never uses (see <see cref="IMindMapRepository.GetVersionSummariesAsync"/>).
/// </summary>
public class MindMapVersionSummary
{
    public int VersionNumber { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? TriggerMeetingId { get; set; }
}
