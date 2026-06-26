using Anela.Heblo.Application.Features.Article.Admin;
using Anela.Heblo.Application.Features.Article.UseCases.GenerateArticle;
using Anela.Heblo.Application.Features.Article.UseCases.GetArticle;
using Anela.Heblo.Application.Features.Article.UseCases.GetArticleTrace;
using Anela.Heblo.Application.Features.Article.UseCases.GetFeedbackList;
using Anela.Heblo.Application.Features.Article.UseCases.ListArticles;
using Anela.Heblo.Application.Features.Article.UseCases.SubmitFeedback;
using Anela.Heblo.Domain.Features.Article;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[FeatureAuthorize(Feature.Marketing_Article)]
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
    [FeatureAuthorize(Feature.Marketing_Article, AccessLevel.Write)]
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

    [HttpGet("{id:guid}/trace")]
    public async Task<ActionResult<GetArticleTraceResponse>> GetTrace(
        Guid id,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetArticleTraceRequest { Id = id }, ct);
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

    [ProducesResponseType(typeof(SubmitArticleFeedbackResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SubmitArticleFeedbackResponse), StatusCodes.Status409Conflict)]
    [HttpPost("{id:guid}/feedback")]
    public async Task<ActionResult<SubmitArticleFeedbackResponse>> SubmitFeedback(
        Guid id,
        [FromBody] SubmitArticleFeedbackRequest request,
        CancellationToken ct = default)
    {
        request.ArticleId = id;
        var result = await _mediator.Send(request, ct);
        return HandleResponse(result);
    }

    [HttpGet("feedback/list")]
    [FeatureAuthorize(Feature.Marketing_Article, AccessLevel.Write)]
    public async Task<ActionResult<GetArticleFeedbackListResponse>> FeedbackList(
        [FromQuery] bool? hasFeedback = null,
        [FromQuery] string? requestedBy = null,
        [FromQuery] string sortBy = "CreatedAt",
        [FromQuery] bool sortDescending = true,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetArticleFeedbackListRequest
        {
            HasFeedback = hasFeedback,
            RequestedBy = requestedBy,
            SortBy = sortBy,
            SortDescending = sortDescending,
            Page = page,
            PageSize = pageSize,
        }, ct);
        return HandleResponse(result);
    }

    [HttpPost("admin/backfill-requested-by")]
    [FeatureAuthorize(Feature.Admin_Administration, AccessLevel.Write)]
    public async Task<ActionResult<BackfillArticleRequestedByResponse>> BackfillRequestedBy(
        [FromBody] BackfillArticleRequestedByCommand request,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(request, ct);
        return HandleResponse(result);
    }
}
