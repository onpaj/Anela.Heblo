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

    public async Task<object> LoadDataAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().Date);
            var yesterday = today.AddDays(-1);

            var startDate = yesterday.ToDateTime(TimeOnly.MinValue);
            var endDate = yesterday.ToDateTime(TimeOnly.MaxValue);

            var statistics = await _repository.GetImportStatisticsAsync(
                startDate,
                endDate,
                "ImportDate");

            var yesterdayStats = statistics.FirstOrDefault();
            var itemCount = yesterdayStats?.TotalItemCount ?? 0;

            return new
            {
                status = "success",
                data = new
                {
                    count = itemCount,
                    date = yesterday.ToString("dd.MM.yyyy")
                },
                metadata = new
                {
                    lastUpdated = DateTime.UtcNow,
                    source = "BankStatementImportRepository"
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
