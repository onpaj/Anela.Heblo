using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Xcc.Services.Dashboard;

namespace Anela.Heblo.Application.Features.Transport.Dashboard;

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

    public async Task<object> LoadDataAsync(CancellationToken cancellationToken = default)
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
}
