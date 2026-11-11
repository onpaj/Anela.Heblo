namespace Anela.Heblo.Application.Features.Manufacture.Contracts;

/// <summary>
/// Manufacture-owned mirror of the Catalog product type. Names match Catalog's ProductType;
/// the Catalog-side mapper converts explicitly and fails loudly on unknown values.
/// </summary>
public enum ProductKind
{
    UNDEFINED = 0,
    Goods = 1,
    Material = 3,
    SemiProduct = 7,
    Product = 8,
    Set = 99,
}
