using MediatR;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.UpsertOvertimeEmployee;

public class UpsertOvertimeEmployeeRequest : IRequest<UpsertOvertimeEmployeeResponse>
{
    public Guid PersonId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public decimal BaselineHours { get; set; }
    public DateOnly BaselineDate { get; set; }
    public bool IsActive { get; set; }
}
