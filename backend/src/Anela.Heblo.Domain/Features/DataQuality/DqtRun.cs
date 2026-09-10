using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.DataQuality;

public class DqtRun : Entity<Guid>
{
    public DqtTestType TestType { get; private set; }
    public DateOnly DateFrom { get; private set; }
    public DateOnly DateTo { get; private set; }
    public DqtRunStatus Status { get; private set; }
    public DateTime StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public DqtTriggerType TriggerType { get; private set; }
    public int TotalChecked { get; private set; }
    public int TotalMismatches { get; private set; }
    public string? ErrorMessage { get; private set; }

    public List<InvoiceDqtResult> Results { get; private set; } = new();

    private DqtRun() { } // EF Core

    public static DqtRun Start(DqtTestType testType, DateOnly dateFrom, DateOnly dateTo, DqtTriggerType triggerType, DateTime startedAt)
    {
        return new DqtRun
        {
            Id = Guid.NewGuid(),
            TestType = testType,
            DateFrom = dateFrom,
            DateTo = dateTo,
            Status = DqtRunStatus.Running,
            StartedAt = startedAt,
            TriggerType = triggerType
        };
    }

    public void Complete(int totalChecked, int totalMismatches, DateTime completedAt)
    {
        Status = DqtRunStatus.Completed;
        CompletedAt = completedAt;
        TotalChecked = totalChecked;
        TotalMismatches = totalMismatches;
    }

    public void Fail(string errorMessage, DateTime completedAt)
    {
        Status = DqtRunStatus.Failed;
        CompletedAt = completedAt;
        ErrorMessage = errorMessage;
    }
}
