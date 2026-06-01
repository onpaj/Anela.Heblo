namespace Anela.Heblo.Application.Features.Manufacture.UseCases.GetCalendarView;

public class CalendarEventSemiProductDto
{
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal PlannedQuantity { get; set; }
    public decimal? ActualQuantity { get; set; }
    public decimal BatchMultiplier { get; set; }
}