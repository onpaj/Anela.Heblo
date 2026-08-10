namespace Anela.Heblo.Application.Features.Attendance.Overtime.Contracts;

public class OvertimeEmployeeDto
{
    public Guid PersonId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public decimal BaselineHours { get; set; }
    public DateOnly BaselineDate { get; set; }
    public bool IsActive { get; set; }
    public decimal CurrentBalance { get; set; }
}

public class AvailableLogetoPersonDto
{
    public Guid PersonId { get; set; }
    public string FullName { get; set; } = string.Empty;
}
