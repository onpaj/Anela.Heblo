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
    public async Task<ActionResult<GetExpeditionDatesResponse>> GetDates([FromQuery] GetExpeditionDatesRequest request)
    {
        var response = await _mediator.Send(request);
        return Ok(response);
    }

    [HttpGet("{date}")]
    public async Task<ActionResult<GetExpeditionListsByDateResponse>> GetByDate(string date)
    {
        var response = await _mediator.Send(new GetExpeditionListsByDateRequest { Date = date });
        return Ok(response);
    }

    [HttpGet("download/{*blobPath}")]
    public async Task<IActionResult> Download(string blobPath)
    {
        try
        {
            var response = await _mediator.Send(new DownloadExpeditionListRequest { BlobPath = blobPath });
            return File(response.Stream, response.ContentType, response.FileName, enableRangeProcessing: false);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("reprint")]
    public async Task<ActionResult<ReprintExpeditionListResponse>> Reprint([FromBody] ReprintExpeditionListRequest request)
    {
        try
        {
            var response = await _mediator.Send(request);
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
