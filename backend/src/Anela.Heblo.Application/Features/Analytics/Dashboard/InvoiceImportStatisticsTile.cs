using Anela.Heblo.Application.Features.Analytics.Infrastructure;
using Anela.Heblo.Application.Features.Analytics.UseCases.GetInvoiceImportStatistics;
using Anela.Heblo.Xcc.Services.Dashboard;

namespace Anela.Heblo.Application.Features.Analytics.Dashboard;

public class InvoiceImportStatisticsTile : ITile
{
    private readonly IAnalyticsRepository _analyticsRepository;
    private readonly TimeProvider _timeProvider;

    public string Title => "Faktury importované včera";
    public string Description => "Počet faktur naimportovaných včera";
    public TileSize Size => TileSize.Small;
    public TileCategory Category => TileCategory.Finance;
    public bool DefaultEnabled => true;
    public bool AutoShow => true;
    public Type ComponentType => typeof(object);
    public string[] RequiredPermissions => Array.Empty<string>();

    public InvoiceImportStatisticsTile(
        IAnalyticsRepository analyticsRepository,
        TimeProvider timeProvider)
    {
        _analyticsRepository = analyticsRepository;
        _timeProvider = timeProvider;
    }

    public async Task<object> LoadDataAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().Date);
            var yesterday = today.AddDays(-1);

            var startDate = yesterday.ToDateTime(TimeOnly.MinValue);
            var endDate = yesterday.ToDateTime(TimeOnly.MaxValue);

            var statistics = await _analyticsRepository.GetInvoiceImportStatisticsAsync(
                startDate,
                endDate,
                ImportDateType.LastSyncTime,
                cancellationToken);

            var yesterdayCount = statistics.FirstOrDefault()?.Count ?? 0;

            return new
            {
                status = "success",
                data = new
                {
                    count = yesterdayCount,
                    date = yesterday.ToString("dd.MM.yyyy")
                },
                metadata = new
                {
                    lastUpdated = DateTime.UtcNow,
                    source = "AnalyticsRepository"
                }
            };
        }
        catch (Exception ex)
        {
            return new
            {
                status = "error",
                error = "Nepodařilo se načíst statistiky importu faktur",
                details = ex.Message
            };
        }
    }
}
