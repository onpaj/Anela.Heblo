using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.FinancialOverview;

public class FinancialAnalysisBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<FinancialAnalysisBackgroundService> _logger;
    private readonly FinancialAnalysisOptions _options;

    public FinancialAnalysisBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<FinancialAnalysisBackgroundService> logger,
        IOptions<FinancialAnalysisOptions> options)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Financial Analysis Background Service started");

        if (_options.RefreshInterval != TimeSpan.Zero)
        {
            // Initial load on startup
            await RefreshFinancialDataAsync(stoppingToken);
        }
        

        var lastRefresh = DateTime.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;

                // Check if refresh is needed (every 3 hours)
                if (_options.RefreshInterval != TimeSpan.Zero && now - lastRefresh >= _options.RefreshInterval)
                {
                    await RefreshFinancialDataAsync(stoppingToken);
                    lastRefresh = now;
                }

                // Wait before next cycle (check every 15 minutes)
                await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in Financial Analysis Background Service");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }

        _logger.LogInformation("Financial Analysis Background Service stopped");
    }

    private async Task RefreshFinancialDataAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting financial analysis data refresh via background service");

            using var scope = _serviceProvider.CreateScope();
            var financialAnalysisService = scope.ServiceProvider.GetRequiredService<IFinancialAnalysisService>();

            // Calculate date range - load data for the configured number of months
            var endDate = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1).AddDays(-1); // Last day of previous month
            var startDate = endDate.AddMonths(-_options.MonthsToCache + 1);
            startDate = new DateTime(startDate.Year, startDate.Month, 1);

            _logger.LogInformation("Refreshing financial data from {StartDate} to {EndDate}", startDate, endDate);

            // Delegate to the service to handle the refresh
            await financialAnalysisService.RefreshFinancialDataAsync(startDate, endDate, cancellationToken);

            _logger.LogInformation("Financial analysis data refresh completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh financial analysis data");
            // Don't rethrow - background service should continue running
        }
    }

}