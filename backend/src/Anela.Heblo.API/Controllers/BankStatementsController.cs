using Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementImportStatistics;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BankStatementsController : ControllerBase
{
    private readonly IMediator _mediator;

    public BankStatementsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Gets bank statement import statistics for chart visualization
    /// </summary>
    /// <param name="startDate">Optional start date filter</param>
    /// <param name="endDate">Optional end date filter</param>
    /// <returns>Bank statement import statistics grouped by date</returns>
    [HttpGet("statistics")]
    public async Task<ActionResult<GetBankStatementImportStatisticsResponse>> GetStatistics(
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        var request = new GetBankStatementImportStatisticsRequest
        {
            StartDate = startDate,
            EndDate = endDate
        };

        var response = await _mediator.Send(request);
        return Ok(response);
    }
}