using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Features.Leaflet.UseCases.DeleteLeafletDocument;
using Anela.Heblo.Application.Features.Leaflet.UseCases.GenerateLeaflet;
using Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletChunkDetail;
using Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletDocumentContentTypes;
using Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletDocuments;
using Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletFeedbackList;
using Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletGeneration;
using Anela.Heblo.Application.Features.Leaflet.UseCases.SubmitLeafletFeedback;
using Anela.Heblo.Application.Features.Leaflet.UseCases.UploadLeaflet;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[GateOn(Feature.Marketing_Leaflet)]
[ApiController]
[Route("api/leaflet")]
[Authorize(Roles = AccessRoles.MarketingLeafletRead)]
public class LeafletController : BaseApiController
{
    private readonly IMediator _mediator;

    public LeafletController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("generate")]
    [Authorize(Roles = AccessRoles.MarketingLeafletWrite)]
    [ProducesResponseType(typeof(GenerateLeafletResponse), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    [ProducesResponseType(typeof(ProblemDetails), 502)]
    public async Task<IActionResult> Generate([FromBody] GenerateLeafletRequest request, CancellationToken ct)
    {
        try
        {
            var response = await _mediator.Send(request, ct);
            return Ok(response);
        }
        catch (EmptyRetrievalException ex)
        {
            return UnprocessableEntity(new ProblemDetails
            {
                Status = 422,
                Title = "Insufficient knowledge",
                Detail = ex.Message,
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Leaflet generation failed");
            return StatusCode(502, new ProblemDetails
            {
                Status = 502,
                Title = "Generation failed",
                Detail = "Leaflet generation failed. Please try again.",
            });
        }
    }

    [HttpGet("documents")]
    public async Task<ActionResult<GetLeafletDocumentsResponse>> GetDocuments(
        [FromQuery][Range(1, int.MaxValue)] int pageNumber = 1,
        [FromQuery][Range(1, 100)] int pageSize = 20,
        [FromQuery] string sortBy = "IngestedAt",
        [FromQuery] bool sortDescending = true,
        [FromQuery] string? filenameFilter = null,
        [FromQuery] string? statusFilter = null,
        [FromQuery] string? contentTypeFilter = null,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetLeafletDocumentsRequest
        {
            PageNumber = pageNumber,
            PageSize = pageSize,
            SortBy = sortBy,
            SortDescending = sortDescending,
            FilenameFilter = filenameFilter,
            StatusFilter = statusFilter,
            ContentTypeFilter = contentTypeFilter,
        }, ct);
        return HandleResponse(result);
    }

    [HttpGet("documents/content-types")]
    public async Task<ActionResult<GetLeafletDocumentContentTypesResponse>> GetDocumentContentTypes(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetLeafletDocumentContentTypesRequest(), ct);
        return HandleResponse(result);
    }

    [HttpGet("chunks/{id:guid}")]
    public async Task<ActionResult<GetLeafletChunkDetailResponse>> GetChunkDetail(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetLeafletChunkDetailRequest { ChunkId = id }, ct);
        return HandleResponse(result);
    }

    [HttpDelete("documents/{id:guid}")]
    [Authorize(Roles = AccessRoles.MarketingLeafletWrite)]
    public async Task<ActionResult<DeleteLeafletDocumentResponse>> DeleteDocument(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new DeleteLeafletDocumentRequest { DocumentId = id }, ct);
        return HandleResponse(result);
    }

    [HttpPost("documents/upload")]
    [Authorize(Roles = AccessRoles.MarketingLeafletWrite)]
    public async Task<ActionResult<UploadLeafletResponse>> UploadDocument(
        IFormFile file,
        CancellationToken ct = default)
    {
        if (file is null)
            return BadRequest(new UploadLeafletResponse { Success = false, ErrorCode = ErrorCodes.RequiredFieldMissing });

        await using var stream = file.OpenReadStream();
        var result = await _mediator.Send(new UploadLeafletRequest
        {
            FileStream = stream,
            Filename = file.FileName,
            ContentType = file.ContentType,
            FileSizeBytes = file.Length,
        }, ct);
        return HandleResponse(result);
    }

    [HttpPost("feedback")]
    [ProducesResponseType(typeof(SubmitLeafletFeedbackResponse), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public async Task<ActionResult<SubmitLeafletFeedbackResponse>> SubmitFeedback(
        [FromBody] SubmitLeafletFeedbackRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(request, ct);
        return HandleResponse(result);
    }

    [HttpGet("feedback/list")]
    [Authorize(Roles = AccessRoles.MarketingLeafletWrite)]
    public async Task<ActionResult<GetLeafletFeedbackListResponse>> GetFeedbackList(
        [FromQuery] bool? hasFeedback = null,
        [FromQuery] string? userId = null,
        [FromQuery] string sortBy = "CreatedAt",
        [FromQuery] bool sortDescending = true,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetLeafletFeedbackListRequest
        {
            HasFeedback = hasFeedback,
            UserId = userId,
            SortBy = sortBy,
            SortDescending = sortDescending,
            PageNumber = pageNumber,
            PageSize = pageSize,
        }, ct);
        return HandleResponse(result);
    }

    [HttpGet("generations/{id:guid}")]
    public async Task<ActionResult<GetLeafletGenerationResponse>> GetGeneration(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetLeafletGenerationRequest { Id = id }, ct);
        return HandleResponse(result);
    }
}
