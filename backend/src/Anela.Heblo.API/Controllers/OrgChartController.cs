using Anela.Heblo.Application.Features.OrgChart.Contracts;
using Anela.Heblo.Application.Features.OrgChart.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

/// <summary>
/// Controller for organizational chart operations
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class OrgChartController : ControllerBase
{
    private readonly IOrgChartService _orgChartService;
    private readonly ILogger<OrgChartController> _logger;

    public OrgChartController(
        IOrgChartService orgChartService,
        ILogger<OrgChartController> logger)
    {
        _orgChartService = orgChartService;
        _logger = logger;
    }

    /// <summary>
    /// Gets the complete organizational structure
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Organization chart with all positions and employees</returns>
    /// <response code="200">Returns the organizational structure</response>
    /// <response code="500">If there was an error fetching the data</response>
    [HttpGet]
    [ProducesResponseType(typeof(OrgChartResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<OrgChartResponse>> GetOrganizationStructure(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Fetching organizational structure");
            var result = await _orgChartService.GetOrganizationStructureAsync(cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching organizational structure");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Failed to fetch organizational structure", message = ex.Message });
        }
    }
}
