using Anela.Heblo.Application.Features.ExpeditionList.UseCases.RunExpeditionListPrintFix;
using Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.DownloadExpeditionList;
using Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.GetExpeditionDates;
using Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.GetExpeditionListsByDate;
using Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.ReprintExpeditionList;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[ApiController]
[Route("api/expedition-list-archive")]
public class ExpeditionListArchiveController : BaseApiController
{
    private readonly IMediator _mediator;

    public ExpeditionListArchiveController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("dates")]
    public async Task<ActionResult<GetExpeditionDatesResponse>> GetDates(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var request = new GetExpeditionDatesRequest { Page = page, PageSize = pageSize };
        var response = await _mediator.Send(request);
        return Ok(response);
    }

    [HttpGet("{date}")]
    public async Task<ActionResult<GetExpeditionListsByDateResponse>> GetByDate(string date)
    {
        var request = new GetExpeditionListsByDateRequest { Date = date };
        var response = await _mediator.Send(request);
        return Ok(response);
    }

    [HttpGet("download/{*blobPath}")]
    public async Task<ActionResult> Download(string blobPath)
    {
        var request = new DownloadExpeditionListRequest { BlobPath = blobPath };
        var response = await _mediator.Send(request);

        if (!response.Success || response.Stream == null)
        {
            return BadRequest(response.ErrorMessage);
        }

        return File(response.Stream, response.ContentType, response.FileName);
    }

    [HttpPost("run-fix")]
    public async Task<ActionResult<RunExpeditionListPrintFixResponse>> RunFix(CancellationToken cancellationToken)
    {
        var request = new RunExpeditionListPrintFixRequest();
        var response = await _mediator.Send(request, cancellationToken);
        return Ok(response);
    }

    [HttpPost("reprint")]
    public async Task<ActionResult<ReprintExpeditionListResponse>> Reprint([FromBody] ReprintExpeditionListRequest request)
    {
        var response = await _mediator.Send(request);

        if (!response.Success)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }
}
