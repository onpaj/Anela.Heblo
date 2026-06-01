namespace Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrder;

public class UpdateManufactureOrderProductDto
{
    public int Id { get; set; }
    public string ProductCode { get; set; } = null!;
    public string ProductName { get; set; } = null!;
    public string SemiProductCode { get; set; } = null!;
    public decimal PlannedQuantity { get; set; }
    public decimal? ActualQuantity { get; set; }
}