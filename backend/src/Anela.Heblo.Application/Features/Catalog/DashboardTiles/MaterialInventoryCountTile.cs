using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.Catalog.DashboardTiles;

/// <summary>
/// Dashboard tile showing count of materials inventoried in the last 30 days.
/// </summary>
public class MaterialInventoryCountTile : InventoryCountTileBase
{
    public MaterialInventoryCountTile(
        ICatalogRepository catalogRepository,
        TimeProvider timeProvider
        ) : base(catalogRepository, timeProvider)
    {
    }

    public override string Title => "Materiály inventarizované (30dní)";
    public override string Description => "Počet materiálů inventarizovaných za posledních 30 dní";
    protected override ProductType TargetProductType => ProductType.Material;
}
