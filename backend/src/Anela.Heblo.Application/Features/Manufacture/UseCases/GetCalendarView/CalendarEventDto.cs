using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.GetCalendarView;

public class CalendarEventDto
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public ManufactureOrderState State { get; set; }
    public string? ResponsiblePerson { get; set; }
    public bool ManualActionRequired { get; set; }
    public string? ErpOrderNumberSemiproduct { get; set; }
    public DateTime? ErpOrderNumberSemiproductDate { get; set; }
    public string? ErpOrderNumberProduct { get; set; }
    public DateTime? ErpOrderNumberProductDate { get; set; }
    public string? ErpDiscardResidueDocumentNumber { get; set; }
    public DateTime? ErpDiscardResidueDocumentNumberDate { get; set; }

    // Extended information for detailed views
    public CalendarEventSemiProductDto? SemiProduct { get; set; }
    public List<CalendarEventProductDto> Products { get; set; } = new();
}