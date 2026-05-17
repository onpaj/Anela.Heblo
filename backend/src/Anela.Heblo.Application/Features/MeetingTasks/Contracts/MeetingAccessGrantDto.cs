namespace Anela.Heblo.Application.Features.MeetingTasks.Contracts;

public class MeetingAccessGrantDto
{
    public string UserEmail { get; set; } = null!;
    public string? UserDisplayName { get; set; }
}
