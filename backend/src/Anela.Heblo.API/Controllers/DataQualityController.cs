using Anela.Heblo.Application.Features.DataQuality.UseCases.GetDqtRunDetail;
using Anela.Heblo.Application.Features.DataQuality.UseCases.GetDqtRuns;
using Anela.Heblo.Application.Features.DataQuality.UseCases.RunDqt;
using Anela.Heblo.Domain.Features.DataQuality;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[FeatureAuthorize(Feature.Admin_DataQuality)]
[ApiController]
[Route("api/data-quality")]
public class DataQualityController : BaseApiController
{
    private readonly IMediator _mediator;

    public DataQualityController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get paginated list of DQT runs with optional filtering
    /// </summary>
    [HttpGet("runs")]
    public async Task<ActionResult<GetDqtRunsResponse>> GetRuns(
        [FromQuery] DqtTestType? testType = null,
        [FromQuery] DqtRunStatus? status = null,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20)
    {
        var request = new GetDqtRunsRequest
        {
            TestType = testType,
            Status = status,
            PageNumber = pageNumber,
            PageSize = pageSize
        };

        var response = await _mediator.Send(request);
        return HandleResponse(response);
    }

    /// <summary>
    /// Get detail of a specific DQT run including per-invoice results
    /// </summary>
    [HttpGet("runs/{id:guid}")]
    public async Task<ActionResult<GetDqtRunDetailResponse>> GetRunDetail(
        Guid id,
        [FromQuery] int resultPage = 1,
        [FromQuery] int resultPageSize = 50)
    {
        var request = new GetDqtRunDetailRequest
        {
            Id = id,
            ResultPage = resultPage,
            ResultPageSize = resultPageSize
        };

        var response = await _mediator.Send(request);
        return HandleResponse(response);
    }

    /// <summary>
    /// Trigger a manual DQT run
    /// </summary>
    [HttpPost("runs")]
    [FeatureAuthorize(Feature.Admin_DataQuality, AccessLevel.Write)]
    public async Task<ActionResult<RunDqtResponse>> RunDqt([FromBody] RunDqtRequest request)
    {
        var response = await _mediator.Send(request);
        return HandleResponse(response);
    }
}
