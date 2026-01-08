using Anela.Heblo.Application.Features.Invoices.Contracts;
using Anela.Heblo.Application.Features.Invoices.UseCases.EnqueueImportInvoices;
using Anela.Heblo.Application.Features.Invoices.UseCases.GetInvoiceImportJobStatus;
using Anela.Heblo.Application.Features.Invoices.UseCases.GetIssuedInvoiceDetail;
using Anela.Heblo.Application.Features.Invoices.UseCases.GetIssuedInvoicesList;
using Anela.Heblo.Application.Features.Invoices.UseCases.GetIssuedInvoiceSyncStats;
using Anela.Heblo.Application.Features.Invoices.UseCases.GetRunningInvoiceImportJobs;
using Anela.Heblo.Xcc.Services;
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

    [HttpPost("import/enqueue-async")]
    public async Task<ActionResult<EnqueueImportInvoicesResponse>> EnqueueImportInvoicesAsync(
        [FromBody] EnqueueImportInvoicesRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(request, cancellationToken);
        return Ok(result);
    }

    [HttpGet("import/job-status/{jobId}")]
    public async Task<ActionResult<BackgroundJobInfo?>> GetInvoiceImportJobStatus(
        string jobId,
        CancellationToken cancellationToken = default)
    {
        var request = new GetInvoiceImportJobStatusRequest { JobId = jobId };
        var result = await _mediator.Send(request, cancellationToken);

        if (result == null)
        {
            return NotFound(new { message = "Job not found" });
        }

        return Ok(result);
    }

    [HttpGet("import/running-jobs")]
    public async Task<ActionResult<IList<BackgroundJobInfo>>> GetRunningInvoiceImportJobs(
        CancellationToken cancellationToken = default)
    {
        var request = new GetRunningInvoiceImportJobsRequest();
        var result = await _mediator.Send(request, cancellationToken);
        return Ok(result);
    }

}