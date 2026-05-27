using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.BackgroundJobs.Contracts;

/// <summary>
/// Request body for updating recurring job CRON expression
/// </summary>
public class UpdateJobCronRequestBody
{
    /// <summary>
    /// The new CRON expression (e.g. "0 3 * * *")
    /// </summary>
    [Required]
    public string CronExpression { get; set; } = string.Empty;
}
