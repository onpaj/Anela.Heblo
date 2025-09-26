namespace Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrder;

public class UpdateManufactureOrderDto
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = null!;
    public DateTime CreatedDate { get; set; }
    public string CreatedByUser { get; set; } = null!;
    public string? ResponsiblePerson { get; set; }
    public DateOnly SemiProductPlannedDate { get; set; }
    public DateOnly ProductPlannedDate { get; set; }
    public string State { get; set; } = null!;
    public DateTime StateChangedAt { get; set; }
    public string StateChangedByUser { get; set; } = null!;
    public UpdateManufactureOrderSemiProductDto SemiProduct { get; set; } = null!;
    public List<UpdateManufactureOrderProductDto> Products { get; set; } = new();
    public List<UpdateManufactureOrderNoteDto> Notes { get; set; } = new();
}