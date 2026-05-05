using Anela.Heblo.Application.Features.Article.UseCases.GenerateArticle;
using Anela.Heblo.Application.Features.Article.UseCases.GetArticle;
using Anela.Heblo.Application.Features.Article.UseCases.ListArticles;
using Anela.Heblo.Domain.Features.Article;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public sealed class ArticlesController : BaseApiController
{
    private readonly IMediator _mediator;

    public ArticlesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("generate")]
    [Authorize(Policy = AuthorizationConstants.Policies.ArticleGenerator)]
    public async Task<ActionResult<GenerateArticleResponse>> Generate(
        [FromBody] GenerateArticleRequest request,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(request, ct);
        return HandleResponse(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GetArticleResponse>> GetById(
        Guid id,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetArticleRequest { Id = id }, ct);
        return HandleResponse(result);
    }

    [HttpGet]
    public async Task<ActionResult<ListArticlesResponse>> List(
        [FromQuery] ArticleStatus? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ListArticlesRequest
        {
            Status = status,
            Page = page,
            PageSize = pageSize
        }, ct);
        return HandleResponse(result);
    }
}
