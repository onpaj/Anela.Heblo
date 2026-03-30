using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.AskQuestion;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.DeleteDocument;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetDocumentContentTypes;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetDocuments;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetFeedbackList;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SubmitFeedback;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.UploadDocument;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class KnowledgeBaseController : BaseApiController
{
    private readonly IMediator _mediator;

    public KnowledgeBaseController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("documents")]
    public async Task<ActionResult<GetDocumentsResponse>> GetDocuments(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string sortBy = "CreatedAt",
        [FromQuery] bool sortDescending = true,
        [FromQuery] string? filenameFilter = null,
        [FromQuery] string? statusFilter = null,
        [FromQuery] string? contentTypeFilter = null,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetDocumentsRequest
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
    public async Task<ActionResult<GetDocumentContentTypesResponse>> GetDocumentContentTypes(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetDocumentContentTypesRequest(), ct);
        return HandleResponse(result);
    }

    [HttpPost("search")]
    public async Task<ActionResult<SearchDocumentsResponse>> Search(
        [FromBody] SearchDocumentsRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(request, ct);
        return HandleResponse(result);
    }

    [HttpPost("ask")]
    public async Task<ActionResult<AskQuestionResponse>> Ask(
        [FromBody] AskQuestionRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(request, ct);
        return HandleResponse(result);
    }

    [HttpDelete("documents/{id:guid}")]
    [Authorize(Policy = AuthorizationConstants.Policies.KnowledgeBaseUpload)]
    public async Task<ActionResult<DeleteDocumentResponse>> DeleteDocument(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new DeleteDocumentRequest { DocumentId = id }, ct);
        return HandleResponse(result);
    }

    [HttpGet("feedback/list")]
    [Authorize(Policy = AuthorizationConstants.Policies.KnowledgeBaseUpload)]
    public async Task<ActionResult<GetFeedbackListResponse>> GetFeedbackList(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string sortBy = "CreatedAt",
        [FromQuery] bool sortDescending = true,
        [FromQuery] bool? hasFeedback = null,
        [FromQuery] string? userId = null,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetFeedbackListRequest
        {
            PageNumber = pageNumber,
            PageSize = pageSize,
            SortBy = sortBy,
            SortDescending = sortDescending,
            HasFeedback = hasFeedback,
            UserId = userId,
        }, ct);
        return HandleResponse(result);
    }

    [HttpPost("feedback")]
    public async Task<ActionResult<SubmitFeedbackResponse>> SubmitFeedback(
        [FromBody] SubmitFeedbackRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(request, ct);
        return HandleResponse(result);
    }

    [HttpPost("documents/upload")]
    [Authorize(Policy = AuthorizationConstants.Policies.KnowledgeBaseUpload)]
    public async Task<ActionResult<UploadDocumentResponse>> UploadDocument(
        IFormFile file,
        [FromForm] string documentType = "KnowledgeBase",
        CancellationToken ct = default)
    {
        if (file is null)
            return BadRequest(new UploadDocumentResponse { Success = false });

        if (!Enum.TryParse<DocumentType>(documentType, ignoreCase: true, out var parsedDocumentType))
            return BadRequest(new UploadDocumentResponse { Success = false });

        await using var stream = file.OpenReadStream();
        var request = new UploadDocumentRequest
        {
            FileStream = stream,
            Filename = file.FileName,
            ContentType = file.ContentType,
            FileSizeBytes = file.Length,
            DocumentType = parsedDocumentType,
        };
        var result = await _mediator.Send(request, ct);
        return HandleResponse(result);
    }
}
