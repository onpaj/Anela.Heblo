namespace Anela.Heblo.Application.Features.Manufacture.Services;

public class SubmitManufactureRequestItem
{
    public required string ProductCode { get; set; }
    public required string Name { get; set; }
    public decimal Amount { get; set; }
}