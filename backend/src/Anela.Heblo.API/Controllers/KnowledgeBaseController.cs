using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.AskQuestion;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.DeleteDocument;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetDocuments;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.UploadDocument;
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
    public async Task<ActionResult<GetDocumentsResponse>> GetDocuments(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetDocumentsRequest(), ct);
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
    [Authorize(Policy = "KnowledgeBaseUpload")]
    public async Task<ActionResult<DeleteDocumentResponse>> DeleteDocument(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new DeleteDocumentRequest { DocumentId = id }, ct);
        return HandleResponse(result);
    }

    [HttpPost("documents/upload")]
    [Authorize(Policy = "KnowledgeBaseUpload")]
    public async Task<ActionResult<UploadDocumentResponse>> UploadDocument(
        IFormFile file,
        CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        var request = new UploadDocumentRequest
        {
            FileStream = stream,
            Filename = file.FileName,
            ContentType = file.ContentType,
            FileSizeBytes = file.Length,
        };
        var result = await _mediator.Send(request, ct);
        return HandleResponse(result);
    }
}