namespace Anela.Heblo.Domain.Features.Manufacture;

public enum ManufactureOrderState
{
    Draft = 1,
    SemiProductPlanned = 2,
    SemiProductManufacture = 3,
    ProductsPlanned = 4,
    ProductsManufacture = 5,
    Completed = 6,
    Cancelled = 7
}