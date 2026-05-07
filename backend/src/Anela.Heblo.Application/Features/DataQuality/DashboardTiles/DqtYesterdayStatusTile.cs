using Anela.Heblo.Domain.Features.DataQuality;
using Anela.Heblo.Xcc.Services.Dashboard;

namespace Anela.Heblo.Application.Features.DataQuality.DashboardTiles;

public class DqtYesterdayStatusTile : ITile
{
    private readonly IDqtRunRepository _repository;
    private readonly TimeProvider _timeProvider;

    public string Title => "DQT včera";
    public string Description => "Stav včerejšího DQT testu faktur";
    public TileSize Size => TileSize.Small;
    public TileCategory Category => TileCategory.DataQuality;
    public bool DefaultEnabled => true;
    public bool AutoShow => false;
    public Type ComponentType => typeof(object);
    public string[] RequiredPermissions => Array.Empty<string>();

    public DqtYesterdayStatusTile(IDqtRunRepository repository, TimeProvider timeProvider)
    {
        _repository = repository;
        _timeProvider = timeProvider;
    }

    public async Task<object> LoadDataAsync(Dictionary<string, string>? parameters = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var yesterday = DateOnly.FromDateTime(_timeProvider.GetUtcNow().Date.AddDays(-1));
            var run = await _repository.GetLatestByTestTypeAsync(DqtTestType.IssuedInvoiceComparison, cancellationToken);

            if (run is null || run.DateFrom != yesterday)
            {
                return new
                {
                    status = "no_data",
                    data = (object?)null,
                    drillDown = new { href = "/data-quality", enabled = true }
                };
            }

            var statusStr = run.Status == DqtRunStatus.Failed
                ? "error"
                : run.TotalMismatches > 0
                    ? "warning"
                    : "success";

            return new
            {
                status = statusStr,
                data = new
                {
                    runId = run.Id,
                    runStatus = run.Status.ToString(),
                    dateFrom = run.DateFrom,
                    dateTo = run.DateTo,
                    totalChecked = run.TotalChecked,
                    totalMismatches = run.TotalMismatches
                },
                drillDown = new { href = "/data-quality", enabled = true }
            };
        }
        catch
        {
            return new
            {
                status = "error",
                data = (object?)null,
                drillDown = new { href = "/data-quality", enabled = true }
            };
        }
    }
}
