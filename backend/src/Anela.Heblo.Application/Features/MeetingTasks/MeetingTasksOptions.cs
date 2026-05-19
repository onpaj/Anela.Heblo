using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.MeetingTasks;

public class MeetingTasksOptions
{
    public const string SectionName = "MeetingTasks";

    // [Required] rejects null/empty. In non-mock-auth mode MeetingTasksModule also validates
    // that the value is not the "CONFIGURE_IN_USER_SECRETS" placeholder.
    [Required]
    public string PlannerPlanId { get; set; } = string.Empty;

    public string? PlannerBucketId { get; set; }

    /// <summary>
    /// Path to the static user-directory JSON file. Relative paths are resolved
    /// against the application base directory.
    /// </summary>
    public string UserDirectoryPath { get; set; } = "meeting-users.json";

    /// <summary>
    /// How many days back the Plaud polling job looks for recordings to ingest.
    /// </summary>
    public int MaxRecordingAgeDays { get; set; } = 7;
}
