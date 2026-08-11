using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Attendance.Overtime;
using MediatR;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.SetStatementReviewed;

public class SetStatementReviewedHandler : IRequestHandler<SetStatementReviewedRequest, SetStatementReviewedResponse>
{
    private readonly IOvertimeStatementRepository _statements;

    public SetStatementReviewedHandler(IOvertimeStatementRepository statements)
    {
        _statements = statements;
    }

    public async Task<SetStatementReviewedResponse> Handle(SetStatementReviewedRequest request, CancellationToken cancellationToken)
    {
        var monthStatements = await _statements.GetByMonthAsync(request.Year, request.Month, cancellationToken);

        if (monthStatements.Any(s => s.Status == OvertimeStatementStatus.Closed))
        {
            return new SetStatementReviewedResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.OvertimeMonthAlreadyClosed,
                Params = new Dictionary<string, string>
                {
                    { "year", request.Year.ToString() },
                    { "month", request.Month.ToString() }
                }
            };
        }

        var statement = monthStatements.FirstOrDefault(s => s.PersonId == request.PersonId);
        if (statement is null)
        {
            return new SetStatementReviewedResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.OvertimeEmployeeNotFound
            };
        }

        statement.IsReviewed = request.IsReviewed;
        await _statements.SaveChangesAsync(cancellationToken);

        return new SetStatementReviewedResponse();
    }
}
