using Anela.Heblo.Application.Features.Audit.Model;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuditController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<AuditController> _logger;

    public AuditController(IMediator mediator, ILogger<AuditController> logger)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [HttpGet("data-loads")]
    public async Task<IActionResult> GetDataLoadAuditLogs(
        [FromQuery] int? limit = 100,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null)
    {
        try
        {
            var request = new GetAuditLogsRequest
            {
                Limit = limit,
                FromDate = fromDate,
                ToDate = toDate
            };

            var response = await _mediator.Send(request);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving data load audit logs");
            return StatusCode(500, new
            {
                Message = "Error retrieving audit logs",
                Error = ex.Message
            });
        }
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetAuditSummary([FromQuery] DateTime? fromDate = null, [FromQuery] DateTime? toDate = null)
    {
        try
        {
            var request = new GetAuditSummaryRequest
            {
                FromDate = fromDate,
                ToDate = toDate
            };

            var response = await _mediator.Send(request);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving audit summary");
            return StatusCode(500, new
            {
                Message = "Error retrieving audit summary",
                Error = ex.Message
            });
        }
    }
}