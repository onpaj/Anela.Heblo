using Anela.Heblo.Domain.Features.Purchase;
using Anela.Heblo.Xcc.Services.Dashboard;

namespace Anela.Heblo.Application.Features.Dashboard.Tiles;

public class PurchaseOrdersInTransitTile : ITile
{
    private readonly IPurchaseOrderRepository _purchaseOrderRepository;

    // Self-describing metadata
    public string Title => "Suma nákupních objednávek";
    public string Description => "Celková částka nákupních objednávek ve stavu 'v přepravě'";
    public TileSize Size => TileSize.Small;
    public TileCategory Category => TileCategory.Purchase;
    public bool DefaultEnabled => true;
    public bool AutoShow => false; // Manual show
    public Type ComponentType => typeof(object); // Frontend component type not needed for backend
    public string[] RequiredPermissions => Array.Empty<string>();

    public PurchaseOrdersInTransitTile(IPurchaseOrderRepository purchaseOrderRepository)
    {
        _purchaseOrderRepository = purchaseOrderRepository;
    }

    public async Task<object> LoadDataAsync(Dictionary<string, string>? parameters = null, CancellationToken cancellationToken = default)
    {
        // Get all purchase orders in transit status
        var inTransitOrders = await _purchaseOrderRepository.GetByStatusAsync(PurchaseOrderStatus.InTransit, cancellationToken);

        // Calculate total amount
        var totalAmount = inTransitOrders.Sum(order => order.TotalAmount);

        // Format amount in thousands with 'k' suffix
        var formattedAmount = FormatAmountInThousands(totalAmount);

        var result = new
        {
            data = new
            {
                count = inTransitOrders.Count(),
                totalAmount = totalAmount,
                formattedAmount = formattedAmount
            },
        };

        return result;
    }

    private string FormatAmountInThousands(decimal amount)
    {
        if (amount == 0)
            return "0";

        var amountInThousands = amount / 1000m;

        // Round to 1 decimal place if needed, otherwise show as integer
        if (amountInThousands % 1 == 0)
            return $"{(int)amountInThousands}k";
        else
            return $"{amountInThousands:F1}k";
    }
}