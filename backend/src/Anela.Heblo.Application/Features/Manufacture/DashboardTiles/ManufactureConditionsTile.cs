using Anela.Heblo.Domain.Features.Manufacture.Conditions;
using Anela.Heblo.Xcc.Services.Dashboard;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Manufacture.DashboardTiles;

[TileId("manufactureconditions")]
public class ManufactureConditionsTile : ITile
{
    private readonly IConditionsReadingProvider _provider;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ManufactureConditionsTile> _logger;

    public string Title => "Podmínky ve výrobně";
    public string Description => "Aktuální teplota a vlhkost (vnitřní / venkovní)";
    public TileSize Size => TileSize.Medium;
    public TileCategory Category => TileCategory.Manufacture;
    public bool DefaultEnabled => true;
    public bool AutoShow => true;
    public Type ComponentType => typeof(object);
    public string[] RequiredPermissions => Array.Empty<string>();

    public ManufactureConditionsTile(
        IConditionsReadingProvider provider,
        TimeProvider timeProvider,
        ILogger<ManufactureConditionsTile> logger)
    {
        _provider = provider;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<object> LoadDataAsync(
        Dictionary<string, string>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var snapshot = await _provider.GetCurrentSnapshotAsync(cancellationToken);
            return BuildResponse(snapshot);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to load HomeAssistant conditions for dashboard tile");
            return BuildResponse(new ConditionsSnapshot(
                null, null, null, null,
                _timeProvider.GetUtcNow().UtcDateTime,
                ConditionsReadingSource.Unavailable));
        }
    }

    private static object BuildResponse(ConditionsSnapshot snapshot) => new
    {
        status = "success",
        data = new
        {
            innerTemperature = snapshot.InnerTemperature,
            innerHumidity = snapshot.InnerHumidity,
            outerTemperature = snapshot.OuterTemperature,
            outerHumidity = snapshot.OuterHumidity,
            recordedAt = snapshot.RecordedAt,
            source = snapshot.Source.ToString()
        },
        metadata = new
        {
            lastUpdated = snapshot.RecordedAt,
            source = "HomeAssistant"
        }
    };
}
