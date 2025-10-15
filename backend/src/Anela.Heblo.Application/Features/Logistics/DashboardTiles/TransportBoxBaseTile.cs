using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Xcc.Services.Dashboard;

namespace Anela.Heblo.Application.Features.Logistics.DashboardTiles;

public abstract class TransportBoxBaseTile : ITile
{
    protected readonly ITransportBoxRepository _repository;

    // Self-describing metadata
    public abstract string Title { get; }
    public abstract string Description { get; }
    public TileSize Size => TileSize.Small;
    public TileCategory Category => TileCategory.Warehouse;
    public bool DefaultEnabled => true;
    public bool AutoShow => true;
    public Type ComponentType => typeof(object);
    public string[] RequiredPermissions => Array.Empty<string>();

    protected abstract TransportBoxState[] FilterStates { get; }

    protected TransportBoxBaseTile(ITransportBoxRepository repository)
    {
        _repository = repository;
    }

    public async Task<object> LoadDataAsync(Dictionary<string, string>? parameters = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var boxes = await _repository.FindAsync(
                box => FilterStates.Contains(box.State),
                includeDetails: false,
                cancellationToken: cancellationToken
            );

            var count = boxes.Count();

            return new
            {
                status = "success",
                data = new
                {
                    count = count
                },
                metadata = new
                {
                    lastUpdated = DateTime.UtcNow,
                    source = "TransportBoxRepository"
                },
                drillDown = new
                {
                    url = GenerateDrillDownUrl(),
                    enabled = true,
                    tooltip = $"Zobrazit všechny {Title.ToLower()}"
                }
            };
        }
        catch (Exception ex)
        {
            return new
            {
                status = "error",
                error = "Nepodařilo se načíst počet boxů",
                details = ex.Message
            };
        }
    }

    protected virtual string GenerateDrillDownUrl()
    {
        // Generate URL with state filter(s)
        if (FilterStates.Length == 1)
        {
            return $"/logistics/transport-boxes?state={FilterStates[0]}";
        }
        
        // Multiple states - use ACTIVE for "active" boxes or first state
        if (FilterStates.Length > 1)
        {
            // Check if this represents "active" boxes (non-closed states)
            var isActiveFilter = FilterStates.All(state => state != TransportBoxState.Closed);
            if (isActiveFilter)
            {
                return "/logistics/transport-boxes?state=ACTIVE";
            }
            
            // Default to first state
            return $"/logistics/transport-boxes?state={FilterStates[0]}";
        }

        // Fallback - no filter
        return "/logistics/transport-boxes";
    }
}
