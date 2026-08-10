namespace Anela.Heblo.Application.Features.Attendance.Overtime.Contracts;

public class OvertimeStatementDto
{
    public Guid PersonId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public bool IsReviewed { get; set; }
    public decimal? DailyContractHours { get; set; }
    public decimal RequiredHours { get; set; }
    public decimal WorkedHours { get; set; }
    public decimal VacationHours { get; set; }
    public decimal SickHours { get; set; }
    public decimal DoctorHours { get; set; }
    public decimal CompTimeHours { get; set; }
    public decimal OtherAbsenceHours { get; set; }
    public decimal DeltaHours { get; set; }
    public decimal PreviousBalance { get; set; }
    public decimal AdjustmentsTotal { get; set; }
    public decimal ProjectedBalance { get; set; }
    public List<string> Warnings { get; set; } = new();
    public List<OvertimeAdjustmentDto> Adjustments { get; set; } = new();
}
