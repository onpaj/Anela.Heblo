using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Xcc.Services.Dashboard;

namespace Anela.Heblo.Application.Features.Manufacture.DashboardTiles;

public class ManualActionRequiredTile : ITile
{
    private readonly IManufactureOrderRepository _repository;
    private readonly TimeProvider _timeProvider;

    // Self-describing metadata
    public string Title => "Výrobní příkazy";
    public string Description => "Počet výrobních příkazů vyžadujících manuální zásah";
    public TileSize Size => TileSize.Small;
    public TileCategory Category => TileCategory.Manufacture;
    public bool DefaultEnabled => true;
    public bool AutoShow => true;
    public Type ComponentType => typeof(object);
    public string[] RequiredPermissions => Array.Empty<string>();

    public ManualActionRequiredTile(
        IManufactureOrderRepository repository,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _timeProvider = timeProvider;
    }

    public async Task<object> LoadDataAsync(Dictionary<string, string>? parameters = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var orders = await _repository.GetOrdersAsync(manualActionRequired: true, cancellationToken: cancellationToken);
            var count = orders.Count;

            return new
            {
                status = "success",
                data = new
                {
                    count,
                    date = _timeProvider.GetUtcNow().DateTime
                }
            };
        }
        catch (Exception ex)
        {
            return new
            {
                status = "error",
                error = ex.Message
            };
        }
    }
}