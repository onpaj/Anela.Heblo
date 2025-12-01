using Anela.Heblo.Application.Features.Invoices.Contracts;
using Anela.Heblo.Application.Features.Invoices.UseCases.EnqueueInvoiceImport;
using Anela.Heblo.Application.Features.Invoices.UseCases.GetIssuedInvoiceDetail;
using Anela.Heblo.Application.Features.Invoices.UseCases.GetIssuedInvoicesList;
using Anela.Heblo.Application.Features.Invoices.UseCases.GetIssuedInvoiceSyncStats;
using Anela.Heblo.Application.Features.Invoices.UseCases.ImportInvoices;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[ApiController]
[Route("api/invoices")]
public class InvoicesController : ControllerBase
{
    private readonly IMediator _mediator;

    public InvoicesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<GetIssuedInvoicesListResponse>> GetInvoicesList(
        [FromQuery] GetIssuedInvoicesListRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(request, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<GetIssuedInvoiceDetailResponse>> GetInvoiceDetail(
        string id, 
        [FromQuery] bool withDetails = false,
        CancellationToken cancellationToken = default)
    {
        var request = new GetIssuedInvoiceDetailRequest 
        { 
            InvoiceId = id,
            WithDetails = withDetails 
        };
        var result = await _mediator.Send(request, cancellationToken);
        return Ok(result);
    }

    [HttpGet("stats")]
    public async Task<ActionResult<GetIssuedInvoiceSyncStatsResponse>> GetSyncStats(
        [FromQuery] GetIssuedInvoiceSyncStatsRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(request, cancellationToken);
        return Ok(result);
    }

    [HttpPost("import")]
    public async Task<ActionResult<ImportResultDto>> ImportInvoices(
        [FromBody] ImportInvoicesRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(request, cancellationToken);
        return Ok(result);
    }

    [HttpPost("import/enqueue")]
    public async Task<ActionResult<List<string>>> EnqueueInvoiceImport(
        [FromBody] EnqueueInvoiceImportRequest request,
        CancellationToken cancellationToken = default)
    {
        var jobIds = await _mediator.Send(request, cancellationToken);
        return Ok(jobIds);
    }

    [HttpGet("cash-register")]
    public Task<ActionResult> GetCashRegisterOrders()
    {
        // TODO: CashRegister functionality removed - out of scope
        throw new NotImplementedException("CashRegister functionality is out of scope for this implementation");
    }
}