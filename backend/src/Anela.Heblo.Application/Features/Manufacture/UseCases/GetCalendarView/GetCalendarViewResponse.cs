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
    public ManufactureOrderState State { get; set; }
    public string? ResponsiblePerson { get; set; }

    // Extended information for detailed views
    public CalendarEventSemiProductDto? SemiProduct { get; set; }
    public List<CalendarEventProductDto> Products { get; set; } = new();
}

public class CalendarEventSemiProductDto
{
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal PlannedQuantity { get; set; }
    public decimal? ActualQuantity { get; set; }
    public decimal BatchMultiplier { get; set; }
}

public class CalendarEventProductDto
{
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal PlannedQuantity { get; set; }
    public decimal? ActualQuantity { get; set; }
}