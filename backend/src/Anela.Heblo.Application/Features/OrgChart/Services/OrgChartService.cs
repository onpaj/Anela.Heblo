using System.Text.Json;
using Anela.Heblo.Application.Features.OrgChart.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.OrgChart.Services;

/// <summary>
/// Service for retrieving organizational chart data from external source
/// </summary>
public class OrgChartService : IOrgChartService
{
    private readonly HttpClient _httpClient;
    private readonly OrgChartOptions _options;
    private readonly ILogger<OrgChartService> _logger;

    public OrgChartService(
        HttpClient httpClient,
        IOptions<OrgChartOptions> options,
        ILogger<OrgChartService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OrgChartResponse> GetOrganizationStructureAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fetching organizational structure from {Url}", _options.DataSourceUrl);

            var response = await _httpClient.GetAsync(_options.DataSourceUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var orgChart = JsonSerializer.Deserialize<OrgChartResponse>(content, options);

            if (orgChart == null)
            {
                _logger.LogError("Failed to deserialize organizational structure from {Url}", _options.DataSourceUrl);
                throw new InvalidOperationException("Failed to deserialize organizational structure");
            }

            _logger.LogInformation(
                "Successfully loaded organizational structure: {PositionCount} positions, {EmployeeCount} employees",
                orgChart.Organization.Positions.Count,
                orgChart.Organization.Positions.Sum(p => p.Employees.Count));

            return orgChart;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error while fetching organizational structure from {Url}", _options.DataSourceUrl);
            throw new InvalidOperationException($"Failed to fetch organizational structure: {ex.Message}", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON deserialization error for organizational structure from {Url}", _options.DataSourceUrl);
            throw new InvalidOperationException($"Failed to parse organizational structure: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while fetching organizational structure from {Url}", _options.DataSourceUrl);
            throw;
        }
    }
}
