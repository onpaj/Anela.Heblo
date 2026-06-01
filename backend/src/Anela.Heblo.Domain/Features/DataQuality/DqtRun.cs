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

    public static DqtRun Start(DqtTestType testType, DateOnly dateFrom, DateOnly dateTo, DqtTriggerType triggerType)
    {
        return new DqtRun
        {
            Id = Guid.NewGuid(),
            TestType = testType,
            DateFrom = dateFrom,
            DateTo = dateTo,
            Status = DqtRunStatus.Running,
            StartedAt = DateTime.UtcNow,
            TriggerType = triggerType
        };
    }

    public void Complete(int totalChecked, int totalMismatches)
    {
        Status = DqtRunStatus.Completed;
        CompletedAt = DateTime.UtcNow;
        TotalChecked = totalChecked;
        TotalMismatches = totalMismatches;
    }

    public void Fail(string errorMessage)
    {
        Status = DqtRunStatus.Failed;
        CompletedAt = DateTime.UtcNow;
        ErrorMessage = errorMessage;
    }
}
