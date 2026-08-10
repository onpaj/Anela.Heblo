using MediatR;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.SetStatementReviewed;

public class SetStatementReviewedRequest : IRequest<SetStatementReviewedResponse>
{
    public Guid PersonId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public bool IsReviewed { get; set; }
}
