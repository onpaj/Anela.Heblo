using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.MeetingTasks;

public class MeetingTasksOptions
{
    public const string SectionName = "MeetingTasks";

    // [Required] applies even in mock-auth mode. appsettings.json must always carry a
    // non-empty placeholder so startup validation passes in all environments.
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
