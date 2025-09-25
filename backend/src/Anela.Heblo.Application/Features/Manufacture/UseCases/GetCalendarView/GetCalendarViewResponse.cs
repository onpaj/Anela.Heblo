using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.GetCalendarView;

public class GetCalendarViewResponse : BaseResponse
{
    public List<CalendarEventDto> Events { get; set; } = new();

    public GetCalendarViewResponse()
    {
    }

    public GetCalendarViewResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters)
    {
    }
}