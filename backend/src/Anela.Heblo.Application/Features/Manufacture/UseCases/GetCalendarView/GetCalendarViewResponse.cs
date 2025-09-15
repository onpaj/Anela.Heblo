using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture;

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

public class CalendarEventDto
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public CalendarEventType Type { get; set; }
    public ManufactureOrderState State { get; set; }
    public string? ResponsiblePerson { get; set; }
}