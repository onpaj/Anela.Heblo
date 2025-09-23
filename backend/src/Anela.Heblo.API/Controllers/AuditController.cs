using Anela.Heblo.Application.Features.Audit.UseCases.GetAuditLogs;
using Anela.Heblo.Application.Features.Audit.UseCases.GetAuditSummary;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize] // 🔒 SECURITY FIX: Require authentication for all audit endpoints
public class AuditController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<AuditController> _logger;
    private readonly ICurrentUserService _currentUserService;

    public AuditController(IMediator mediator, ILogger<AuditController> logger, ICurrentUserService currentUserService)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
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
            // 🔒 SECURITY FIX: Don't expose internal error details
            return StatusCode(500, new
            {
                Message = "Error retrieving audit logs"
                // Removed: Error = ex.Message (security risk)
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
            // 🔒 SECURITY FIX: Don't expose internal error details
            return StatusCode(500, new
            {
                Message = "Error retrieving audit summary"
                // Removed: Error = ex.Message (security risk)
            });
        }
    }
}