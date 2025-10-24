namespace Anela.Heblo.Domain.Features.Manufacture;

public enum ManufactureOrderState
{
    Draft = 1,
    Planned = 2,
    InProduction = 3,
    SemiProductManufactured = 4,
    Completed = 5,
    Cancelled = 6
}