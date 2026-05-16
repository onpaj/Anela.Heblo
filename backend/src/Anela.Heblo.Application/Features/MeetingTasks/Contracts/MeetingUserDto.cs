namespace Anela.Heblo.Application.Features.MeetingTasks.Contracts;

public class MeetingUserDto
{
    public string Email { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public List<string> Aliases { get; set; } = new();
}
