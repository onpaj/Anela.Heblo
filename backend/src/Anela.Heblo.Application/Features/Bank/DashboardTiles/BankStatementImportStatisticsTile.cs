using Anela.Heblo.Domain.Features.Bank;
using Anela.Heblo.Xcc.Services.Dashboard;

namespace Anela.Heblo.Application.Features.Bank.DashboardTiles;

public class BankStatementImportStatisticsTile : ITile
{
    private readonly IBankStatementImportRepository _repository;
    private readonly TimeProvider _timeProvider;

    public string Title => "Bankovní výpisy včera";
    public string Description => "Počet položek bankovních výpisů naimportovaných včera";
    public TileSize Size => TileSize.Small;
    public TileCategory Category => TileCategory.Finance;
    public bool DefaultEnabled => true;
    public bool AutoShow => true;
    public Type ComponentType => typeof(object);
    public string[] RequiredPermissions => Array.Empty<string>();

    public BankStatementImportStatisticsTile(
        IBankStatementImportRepository repository,
        TimeProvider timeProvider)
    {
        _repository = repository;
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

            var statistics = await _repository.GetImportStatisticsAsync(
                startDate,
                endDate,
                "ImportDate");

            var targetDateStats = statistics.FirstOrDefault();
            var itemCount = targetDateStats?.TotalItemCount ?? 0;

            return new
            {
                status = "success",
                data = new
                {
                    count = itemCount,
                    date = targetDate.ToString("dd.MM.yyyy")
                },
                metadata = new
                {
                    lastUpdated = DateTime.UtcNow,
                    source = "BankStatementImportRepository",
                    targetDate = targetDate.ToString("yyyy-MM-dd")
                },
                drillDown = new
                {
                    filters = new { },
                    enabled = true,
                    tooltip = "Zobrazit bankovní výpisy"
                }
            };
        }
        catch (Exception ex)
        {
            return new
            {
                status = "error",
                error = "Nepodařilo se načíst statistiky importu bankovních výpisů",
                details = ex.Message
            };
        }
    }
}
