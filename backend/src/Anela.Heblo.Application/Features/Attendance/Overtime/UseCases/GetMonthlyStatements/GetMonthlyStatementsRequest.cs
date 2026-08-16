using MediatR;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.GetMonthlyStatements;

public class GetMonthlyStatementsRequest : IRequest<GetMonthlyStatementsResponse>
{
    public int Year { get; set; }
    public int Month { get; set; }
}
