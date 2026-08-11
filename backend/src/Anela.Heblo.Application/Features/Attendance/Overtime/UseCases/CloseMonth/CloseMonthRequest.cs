using MediatR;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.CloseMonth;

public class CloseMonthRequest : IRequest<CloseMonthResponse>
{
    public int Year { get; set; }
    public int Month { get; set; }

    /// <summary>Close even when some employees are not marked as reviewed.</summary>
    public bool Force { get; set; }
}
