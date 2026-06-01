using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.GetCalendarView;

public class GetCalendarViewRequest : IRequest<GetCalendarViewResponse>
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
}