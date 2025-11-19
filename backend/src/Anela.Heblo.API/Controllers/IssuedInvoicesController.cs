using Anela.Heblo.Application.Features.Invoices.UseCases.GetIssuedInvoiceDetail;
using Anela.Heblo.Application.Features.Invoices.UseCases.GetIssuedInvoicesList;
using Anela.Heblo.Application.Features.Invoices.UseCases.GetIssuedInvoiceSyncStats;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

/// <summary>
/// Controller for managing issued invoices agenda
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class IssuedInvoicesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<IssuedInvoicesController> _logger;

    public IssuedInvoicesController(IMediator mediator, ILogger<IssuedInvoicesController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    /// <summary>
    /// Get paginated list of issued invoices with filtering
    /// </summary>
    /// <param name="pageNumber">Page number (default: 1)</param>
    /// <param name="pageSize">Page size (default: 20)</param>
    /// <param name="sortBy">Sort field (default: InvoiceDate)</param>
    /// <param name="sortDescending">Sort descending (default: true)</param>
    /// <param name="invoiceId">Filter by invoice ID</param>
    /// <param name="customerName">Filter by customer name</param>
    /// <param name="invoiceDateFrom">Filter by invoice date from</param>
    /// <param name="invoiceDateTo">Filter by invoice date to</param>
    /// <param name="isSynced">Filter by sync status</param>
    /// <param name="showOnlyUnsynced">Show only unsynced invoices</param>
    /// <param name="showOnlyWithErrors">Show only invoices with errors</param>
    /// <returns>Paginated list of issued invoices</returns>
    [HttpGet]
    public async Task<ActionResult<GetIssuedInvoicesListResponse>> GetList(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? sortBy = "InvoiceDate",
        [FromQuery] bool sortDescending = true,
        [FromQuery] string? invoiceId = null,
        [FromQuery] string? customerName = null,
        [FromQuery] DateTime? invoiceDateFrom = null,
        [FromQuery] DateTime? invoiceDateTo = null,
        [FromQuery] bool? isSynced = null,
        [FromQuery] bool showOnlyUnsynced = false,
        [FromQuery] bool showOnlyWithErrors = false)
    {
        try
        {
            var request = new GetIssuedInvoicesListRequest
            {
                PageNumber = pageNumber,
                PageSize = pageSize,
                SortBy = sortBy,
                SortDescending = sortDescending,
                InvoiceId = invoiceId,
                CustomerName = customerName,
                InvoiceDateFrom = invoiceDateFrom,
                InvoiceDateTo = invoiceDateTo,
                IsSynced = isSynced,
                ShowOnlyUnsynced = showOnlyUnsynced,
                ShowOnlyWithErrors = showOnlyWithErrors
            };

            var response = await _mediator.Send(request);

            if (!response.Success)
            {
                return BadRequest(response);
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting issued invoices list");
            return StatusCode(500, new { message = "Nastala chyba při načítání seznamu faktur" });
        }
    }

    /// <summary>
    /// Get detailed information about a specific issued invoice
    /// </summary>
    /// <param name="id">Invoice ID</param>
    /// <returns>Detailed invoice information including sync history</returns>
    [HttpGet("{id}")]
    public async Task<ActionResult<GetIssuedInvoiceDetailResponse>> GetDetail(string id)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return BadRequest(new { message = "ID faktury je povinné" });
            }

            var request = new GetIssuedInvoiceDetailRequest { Id = id };
            var response = await _mediator.Send(request);

            if (!response.Success)
            {
                if (response.ErrorCode == Application.Shared.ErrorCodes.ResourceNotFound)
                {
                    return NotFound(response);
                }
                return BadRequest(response);
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting issued invoice detail for ID: {InvoiceId}", id);
            return StatusCode(500, new { message = "Nastala chyba při načítání detailu faktury" });
        }
    }

    /// <summary>
    /// Get synchronization statistics for issued invoices
    /// </summary>
    /// <param name="fromDate">Start date for statistics (default: 30 days ago)</param>
    /// <param name="toDate">End date for statistics (default: today)</param>
    /// <returns>Synchronization statistics</returns>
    [HttpGet("sync-stats")]
    public async Task<ActionResult<GetIssuedInvoiceSyncStatsResponse>> GetSyncStats(
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null)
    {
        try
        {
            var request = new GetIssuedInvoiceSyncStatsRequest
            {
                FromDate = fromDate,
                ToDate = toDate
            };

            var response = await _mediator.Send(request);

            if (!response.Success)
            {
                return BadRequest(response);
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting issued invoice sync stats");
            return StatusCode(500, new { message = "Nastala chyba při načítání statistik synchronizace" });
        }
    }
}