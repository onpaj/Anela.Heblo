namespace Anela.Heblo.Application.Features.BackgroundJobs.Contracts;

/// <summary>
/// Request body for updating recurring job status
/// </summary>
public class UpdateJobStatusRequestBody
{
    /// <summary>
    /// Whether the job should be enabled
    /// </summary>
    public bool IsEnabled { get; set; }
}
