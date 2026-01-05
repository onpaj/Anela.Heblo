namespace Anela.Heblo.Application.Features.BackgroundJobs.UseCases.TriggerRecurringJob;

public class TriggerRecurringJobResponse
{
    public bool Success { get; set; }
    public string? JobId { get; set; }
    public string? ErrorMessage { get; set; }
}
