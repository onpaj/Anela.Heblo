using Anela.Heblo.Application.Features.Analytics.Infrastructure;
using Anela.Heblo.Application.Features.Analytics.UseCases.GetInvoiceImportStatistics;
using Anela.Heblo.Xcc.Services.Dashboard;

namespace Anela.Heblo.Application.Features.Analytics.DashboardTiles;

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

    public async Task<object> LoadDataAsync(Dictionary<string, string>? parameters = null, CancellationToken cancellationToken = default)
    {
        try
        {
            // Extract date parameter from frontend (should be in user's local timezone)
            DateOnly targetDate;
            if (parameters != null && parameters.TryGetValue("date", out var dateStr) &&
                DateOnly.TryParse(dateStr, out var parsedDate))
            {
                targetDate = parsedDate;
            }
            else
            {
                // Fallback to UTC yesterday
                var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().Date);
                targetDate = today.AddDays(-1);
            }

            // Convert target date to UTC DateTime range for database query
            var startDate = targetDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var endDate = targetDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

            var statistics = await _analyticsRepository.GetInvoiceImportStatisticsAsync(
                startDate,
                endDate,
                ImportDateType.LastSyncTime,
                cancellationToken);

            var targetDateCount = statistics.FirstOrDefault()?.Count ?? 0;

            return new
            {
                status = "success",
                data = new
                {
                    count = targetDateCount,
                    date = targetDate.ToString("dd.MM.yyyy")
                },
                metadata = new
                {
                    lastUpdated = DateTime.UtcNow,
                    source = "AnalyticsRepository",
                    targetDate = targetDate.ToString("yyyy-MM-dd")
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
