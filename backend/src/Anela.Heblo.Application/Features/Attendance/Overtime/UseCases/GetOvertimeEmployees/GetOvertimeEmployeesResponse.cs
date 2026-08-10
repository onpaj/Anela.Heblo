using Anela.Heblo.Application.Features.Attendance.Overtime.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.GetOvertimeEmployees;

public class GetOvertimeEmployeesResponse : BaseResponse
{
    public List<OvertimeEmployeeDto> Employees { get; set; } = new();
    public List<AvailableLogetoPersonDto> AvailablePeople { get; set; } = new();
}
