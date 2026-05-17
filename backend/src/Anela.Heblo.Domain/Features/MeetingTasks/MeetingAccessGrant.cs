namespace Anela.Heblo.Domain.Features.MeetingTasks;

public class MeetingAccessGrant
{
    public Guid Id { get; set; }
    public Guid MeetingTranscriptId { get; set; }
    public MeetingTranscript MeetingTranscript { get; set; } = null!;
    public string UserEmail { get; set; } = null!;
    public string? UserDisplayName { get; set; }
    public DateTime GrantedAt { get; set; }
    public string GrantedByUserEmail { get; set; } = null!;
}
