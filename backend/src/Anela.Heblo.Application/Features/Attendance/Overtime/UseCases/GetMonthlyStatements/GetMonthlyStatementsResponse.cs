using Anela.Heblo.Application.Features.Attendance.Overtime.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.GetMonthlyStatements;

public class GetMonthlyStatementsResponse : BaseResponse
{
    public GetMonthlyStatementsResponse() { }
    public GetMonthlyStatementsResponse(Exception ex) : base(ex) { }

    public int Year { get; set; }
    public int Month { get; set; }
    public bool IsClosed { get; set; }
    public List<OvertimeStatementDto> Statements { get; set; } = new();
}
