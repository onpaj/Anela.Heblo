namespace Anela.Heblo.Application.Features.MeetingTasks;

public class MeetingTasksOptions
{
    public const string SectionName = "MeetingTasks";

    public string TodoListName { get; set; } = "Meeting Actions";

    /// <summary>
    /// Path to the static user-directory JSON file. Relative paths are resolved
    /// against the application base directory.
    /// </summary>
    public string UserDirectoryPath { get; set; } = "meeting-users.json";
}
