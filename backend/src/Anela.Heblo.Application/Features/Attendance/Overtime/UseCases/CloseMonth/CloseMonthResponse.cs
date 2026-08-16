using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.CloseMonth;

public class CloseMonthResponse : BaseResponse
{
    public int ClosedCount { get; set; }
    public bool PublishSkipped { get; set; }
    public bool PublishFailed { get; set; }
}
