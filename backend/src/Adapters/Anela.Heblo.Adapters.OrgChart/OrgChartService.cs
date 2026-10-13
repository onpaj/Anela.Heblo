using System.Text.Json;
using Anela.Heblo.Adapters.OrgChart.Models;
using Anela.Heblo.Application.Features.OrgChart;
using Anela.Heblo.Application.Features.OrgChart.Contracts;
using Anela.Heblo.Application.Features.OrgChart.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.OrgChart;

/// <summary>
/// Service for retrieving organizational chart data from external source
/// </summary>
public class OrgChartService : IOrgChartService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

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

            var jsonModel = JsonSerializer.Deserialize<OrgChartJsonModel>(content, JsonOptions);

            if (jsonModel == null)
            {
                throw new InvalidOperationException("Failed to deserialize organizational structure");
            }

            var orgChart = new OrgChartResponse
            {
                Organization = MapOrganization(jsonModel.Organization)
            };

            _logger.LogInformation(
                "Successfully loaded organizational structure: {PositionCount} positions, {EmployeeCount} employees",
                orgChart.Organization.Positions.Count,
                orgChart.Organization.Positions.Sum(p => p.Employees.Count));

            return orgChart;
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to fetch organizational structure: {ex.Message}", ex);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to parse organizational structure: {ex.Message}", ex);
        }
    }

    private static OrganizationDto MapOrganization(OrgChartJsonOrganization? organization)
    {
        return new OrganizationDto
        {
            Name = organization?.Name ?? string.Empty,
            Positions = organization?.Positions?.Select(MapPosition).ToList() ?? new List<PositionDto>()
        };
    }

    private static PositionDto MapPosition(OrgChartJsonPosition position)
    {
        return new PositionDto
        {
            Id = position.Id ?? string.Empty,
            Title = position.Title ?? string.Empty,
            Description = position.Description ?? string.Empty,
            Level = position.Level,
            ParentPositionId = position.ParentPositionId ?? string.Empty,
            Department = position.Department ?? string.Empty,
            Url = position.Url ?? string.Empty,
            Employees = position.Employees?.Select(MapEmployee).ToList() ?? new List<EmployeeDto>()
        };
    }

    private static EmployeeDto MapEmployee(OrgChartJsonEmployee employee)
    {
        return new EmployeeDto
        {
            Id = employee.Id ?? string.Empty,
            Name = employee.Name ?? string.Empty,
            Email = employee.Email ?? string.Empty,
            StartDate = employee.StartDate ?? string.Empty,
            IsPrimary = employee.IsPrimary,
            Url = employee.Url ?? string.Empty
        };
    }
}
