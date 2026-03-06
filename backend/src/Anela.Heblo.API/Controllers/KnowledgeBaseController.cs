using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.AskQuestion;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
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
    private readonly IKnowledgeBaseRepository _repository;

    public KnowledgeBaseController(IMediator mediator, IKnowledgeBaseRepository repository)
    {
        _mediator = mediator;
        _repository = repository;
    }

    [HttpGet("documents")]
    public async Task<IActionResult> GetDocuments(CancellationToken ct)
    {
        var docs = await _repository.GetAllDocumentsAsync(ct);
        return Ok(docs.Select(d => new
        {
            d.Id,
            d.Filename,
            d.Status,
            d.ContentType,
            d.CreatedAt,
            d.IndexedAt
        }));
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
}
