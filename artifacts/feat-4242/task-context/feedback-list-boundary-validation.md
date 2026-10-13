### task: feedback-list-boundary-validation

**Title:** `GetArticleFeedbackList` boundary validation

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Article/UseCases/GetFeedbackList/GetArticleFeedbackListRequestValidator.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Article/Pipeline/ArticleFeedbackListValidationPipelineTests.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Article/ArticleModule.cs`
- Modify: `backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs:84-105`
- Modify: `backend/src/Anela.Heblo.Application/Features/Article/UseCases/GetFeedbackList/GetArticleFeedbackListHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Article/UseCases/GetArticleFeedbackListHandlerTests.cs`

- [ ] **Step 1: Write the failing pipeline test**

Create `backend/test/Anela.Heblo.Tests/Features/Article/Pipeline/ArticleFeedbackListValidationPipelineTests.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.Article.UseCases.GetFeedbackList;
using Anela.Heblo.Application.Shared.Users;
using Anela.Heblo.Domain.Features.Article;
using FluentAssertions;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Article.Pipeline;

/// <summary>
/// Integration tests for the GetArticleFeedbackList validation pipeline behavior.
/// Verifies that ValidationResultBehavior + GetArticleFeedbackListRequestValidator are
/// wired correctly, so an invalid Page/PageSize/SortBy short-circuits before
/// GetArticleFeedbackListHandler.Handle executes, and a valid one reaches it unmodified.
/// </summary>
public class ArticleFeedbackListValidationPipelineTests
{
    private static IMediator BuildMediator(
        Mock<IArticleRepository> repository,
        Mock<IUserDisplayNameResolver> resolver)
    {
        var services = new ServiceCollection();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(GetArticleFeedbackListHandler).Assembly));

        services.AddScoped<IValidator<GetArticleFeedbackListRequest>, GetArticleFeedbackListRequestValidator>();
        services.AddScoped<IPipelineBehavior<GetArticleFeedbackListRequest, GetArticleFeedbackListResponse>,
            ValidationResultBehavior<GetArticleFeedbackListRequest, GetArticleFeedbackListResponse>>();

        services.AddScoped(_ => repository.Object);
        services.AddScoped(_ => resolver.Object);

        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    [Theory]
    [InlineData(0, 20, "CreatedAt")]
    [InlineData(-5, 20, "CreatedAt")]
    [InlineData(1, 30, "CreatedAt")]
    [InlineData(1, 999, "CreatedAt")]
    [InlineData(1, 20, "totallyBogus")]
    public async Task Send_InvalidPageOrPageSizeOrSortBy_ShortCircuits_RepositoryNeverInvoked(
        int page, int pageSize, string sortBy)
    {
        // Arrange
        var repository = new Mock<IArticleRepository>();
        var resolver = new Mock<IUserDisplayNameResolver>();
        var mediator = BuildMediator(repository, resolver);

        var request = new GetArticleFeedbackListRequest { Page = page, PageSize = pageSize, SortBy = sortBy };

        // Act
        var result = await mediator.Send(request);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(Anela.Heblo.Application.Shared.ErrorCodes.ValidationError);
        repository.Verify(
            r => r.GetFeedbackPagedAsync(
                It.IsAny<bool?>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(1, 10, "CreatedAt")]
    [InlineData(1, 20, "PrecisionScore")]
    [InlineData(2, 50, "StyleScore")]
    public async Task Send_ValidPageAndPageSizeAndSortBy_ReachesHandler_ReturnsSuccess(
        int page, int pageSize, string sortBy)
    {
        // Arrange
        var repository = new Mock<IArticleRepository>();
        repository
            .Setup(r => r.GetFeedbackPagedAsync(
                It.IsAny<bool?>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<ArticleFeedbackProjection>)new List<ArticleFeedbackProjection>(), 0));
        repository
            .Setup(r => r.GetFeedbackStatsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ArticleFeedbackStats(0, 0, null, null));

        var resolver = new Mock<IUserDisplayNameResolver>();
        resolver
            .Setup(r => r.ResolveAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<string, string?>)new Dictionary<string, string?>());

        var mediator = BuildMediator(repository, resolver);

        var request = new GetArticleFeedbackListRequest { Page = page, PageSize = pageSize, SortBy = sortBy };

        // Act
        var result = await mediator.Send(request);

        // Assert
        result.Success.Should().BeTrue();
        result.Page.Should().Be(page);
        result.PageSize.Should().Be(pageSize);
        repository.Verify(
            r => r.GetFeedbackPagedAsync(
                null, null, sortBy, true, page, pageSize, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails to compile (validator does not exist yet)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~ArticleFeedbackListValidationPipelineTests`
Expected: FAIL — build error, `GetArticleFeedbackListRequestValidator` does not exist.

- [ ] **Step 3: Create the validator**

Create `backend/src/Anela.Heblo.Application/Features/Article/UseCases/GetFeedbackList/GetArticleFeedbackListRequestValidator.cs`:

```csharp
using System.Linq;
using FluentValidation;

namespace Anela.Heblo.Application.Features.Article.UseCases.GetFeedbackList;

public class GetArticleFeedbackListRequestValidator : AbstractValidator<GetArticleFeedbackListRequest>
{
    private static readonly int[] AllowedPageSizes = [10, 20, 50];
    private static readonly string[] AllowedSortColumns = ["CreatedAt", "PrecisionScore", "StyleScore"];

    public GetArticleFeedbackListRequestValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize)
            .Must(AllowedPageSizes.Contains)
            .WithMessage($"PageSize must be one of: {string.Join(", ", AllowedPageSizes)}");
        RuleFor(x => x.SortBy)
            .Must(AllowedSortColumns.Contains)
            .WithMessage($"SortBy must be one of: {string.Join(", ", AllowedSortColumns)}");
    }
}
```

- [ ] **Step 4: Run the test to verify it now compiles and passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~ArticleFeedbackListValidationPipelineTests`
Expected: PASS (all 8 cases across both `[Theory]` methods).

- [ ] **Step 5: Register the validator and pipeline behavior in `ArticleModule`**

Open `backend/src/Anela.Heblo.Application/Features/Article/ArticleModule.cs` (already modified in Task 1, Step 5) and add one more `using` and the second pair of registrations, right after the `ListArticlesRequest` registrations added in Task 1:

```csharp
using Anela.Heblo.Application.Features.Article.UseCases.GetFeedbackList;
```

```csharp
        services.AddScoped<IValidator<GetArticleFeedbackListRequest>, GetArticleFeedbackListRequestValidator>();
        services.AddScoped<
            IPipelineBehavior<GetArticleFeedbackListRequest, GetArticleFeedbackListResponse>,
            ValidationResultBehavior<GetArticleFeedbackListRequest, GetArticleFeedbackListResponse>>();

        return services;
```

(This replaces the bare `return services;` left by Task 1, Step 5 — the two new lines go immediately before it.)

- [ ] **Step 6: Bind the controller action directly to `GetArticleFeedbackListRequest`**

In `backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs`, replace lines 84-105:

```csharp
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
```

with:

```csharp
    [HttpGet("feedback/list")]
    [FeatureAuthorize(Feature.Marketing_Article, AccessLevel.Write)]
    public async Task<ActionResult<GetArticleFeedbackListResponse>> FeedbackList(
        [FromQuery] GetArticleFeedbackListRequest request,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(request, ct);
        return HandleResponse(result);
    }
```

- [ ] **Step 7: Remove the allowlist code from `GetArticleFeedbackListHandler`**

In `backend/src/Anela.Heblo.Application/Features/Article/UseCases/GetFeedbackList/GetArticleFeedbackListHandler.cs`, replace the whole class body:

```csharp
public sealed class GetArticleFeedbackListHandler
    : IRequestHandler<GetArticleFeedbackListRequest, GetArticleFeedbackListResponse>
{
    private static readonly int[] AllowedPageSizes = [10, 20, 50];
    private static readonly string[] AllowedSortColumns = ["CreatedAt", "PrecisionScore", "StyleScore"];

    private readonly IArticleRepository _repository;
    private readonly IUserDisplayNameResolver _userDisplayNameResolver;

    public GetArticleFeedbackListHandler(
        IArticleRepository repository,
        IUserDisplayNameResolver userDisplayNameResolver)
    {
        _repository = repository;
        _userDisplayNameResolver = userDisplayNameResolver;
    }

    public async Task<GetArticleFeedbackListResponse> Handle(
        GetArticleFeedbackListRequest request,
        CancellationToken ct)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = AllowedPageSizes.Contains(request.PageSize) ? request.PageSize : 20;
        var sortBy = AllowedSortColumns.Contains(request.SortBy) ? request.SortBy : "CreatedAt";

        // Queries run sequentially: they share the scoped DbContext, which EF Core
        // forbids issuing concurrent operations on (a Task.WhenAll here throws
        // "A second operation was started on this context instance").
        var (items, totalCount) = await _repository.GetFeedbackPagedAsync(
            request.HasFeedback,
            request.RequestedBy,
            sortBy,
            request.SortDescending,
            page,
            pageSize,
            ct);

        var stats = await _repository.GetFeedbackStatsAsync(ct);

        var userNames = await _userDisplayNameResolver.ResolveAsync(
            items.Select(a => a.RequestedBy).Where(id => id is not null)!,
            ct);

        return new GetArticleFeedbackListResponse
        {
            Items = items.Select(a => new ArticleFeedbackSummary
            {
                Id = a.Id,
                Title = a.Title,
                Topic = a.Topic,
                RequestedBy = a.RequestedBy,
                UserName = a.RequestedBy is not null ? userNames.GetValueOrDefault(a.RequestedBy) : null,
                CreatedAt = a.CreatedAt,
                PrecisionScore = a.PrecisionScore,
                StyleScore = a.StyleScore,
                HasComment = !string.IsNullOrWhiteSpace(a.FeedbackComment),
            }).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            Stats = new ArticleFeedbackStatsDto
            {
                TotalArticles = stats.TotalArticles,
                TotalWithFeedback = stats.TotalWithFeedback,
                AvgPrecisionScore = stats.AvgPrecisionScore is { } p ? Math.Round(p, 1) : null,
                AvgStyleScore = stats.AvgStyleScore is { } s ? Math.Round(s, 1) : null,
            },
        };
    }
}
```

with:

```csharp
public sealed class GetArticleFeedbackListHandler
    : IRequestHandler<GetArticleFeedbackListRequest, GetArticleFeedbackListResponse>
{
    private readonly IArticleRepository _repository;
    private readonly IUserDisplayNameResolver _userDisplayNameResolver;

    public GetArticleFeedbackListHandler(
        IArticleRepository repository,
        IUserDisplayNameResolver userDisplayNameResolver)
    {
        _repository = repository;
        _userDisplayNameResolver = userDisplayNameResolver;
    }

    public async Task<GetArticleFeedbackListResponse> Handle(
        GetArticleFeedbackListRequest request,
        CancellationToken ct)
    {
        // Queries run sequentially: they share the scoped DbContext, which EF Core
        // forbids issuing concurrent operations on (a Task.WhenAll here throws
        // "A second operation was started on this context instance").
        var (items, totalCount) = await _repository.GetFeedbackPagedAsync(
            request.HasFeedback,
            request.RequestedBy,
            request.SortBy,
            request.SortDescending,
            request.Page,
            request.PageSize,
            ct);

        var stats = await _repository.GetFeedbackStatsAsync(ct);

        var userNames = await _userDisplayNameResolver.ResolveAsync(
            items.Select(a => a.RequestedBy).Where(id => id is not null)!,
            ct);

        return new GetArticleFeedbackListResponse
        {
            Items = items.Select(a => new ArticleFeedbackSummary
            {
                Id = a.Id,
                Title = a.Title,
                Topic = a.Topic,
                RequestedBy = a.RequestedBy,
                UserName = a.RequestedBy is not null ? userNames.GetValueOrDefault(a.RequestedBy) : null,
                CreatedAt = a.CreatedAt,
                PrecisionScore = a.PrecisionScore,
                StyleScore = a.StyleScore,
                HasComment = !string.IsNullOrWhiteSpace(a.FeedbackComment),
            }).ToList(),
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize,
            Stats = new ArticleFeedbackStatsDto
            {
                TotalArticles = stats.TotalArticles,
                TotalWithFeedback = stats.TotalWithFeedback,
                AvgPrecisionScore = stats.AvgPrecisionScore is { } p ? Math.Round(p, 1) : null,
                AvgStyleScore = stats.AvgStyleScore is { } s ? Math.Round(s, 1) : null,
            },
        };
    }
}
```

- [ ] **Step 8: Replace the allowlist-assertion handler tests**

In `backend/test/Anela.Heblo.Tests/Article/UseCases/GetArticleFeedbackListHandlerTests.cs`, delete these two test methods entirely (their asserted behavior — the handler silently falling back to a default — no longer exists; it is now covered by `ArticleFeedbackListValidationPipelineTests` from Step 1, which asserts invalid input never reaches the handler at all):
- `Handle_UnknownSortBy_FallsBackToCreatedAt`
- `Handle_PageSizeOutsideAllowlist_FallsBackTo20`

Keep `Handle_DefaultParams_RunsPagedAndStatsInParallelAndProjectsResults` and `Handle_ResolvesUserNameFromRequestedBy` unchanged — they already only use valid/default `Page`/`PageSize`/`SortBy` values and assert the handler passes them through, which remains correct.

- [ ] **Step 9: Run the full Article test suite and build to verify everything passes**

Run: `dotnet build Anela.Heblo.sln`
Expected: Build succeeds.

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~Article`
Expected: PASS — all Article-related tests green, including both new pipeline test files and the trimmed handler test files from Task 1 and this task.

- [ ] **Step 10: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Article/UseCases/GetFeedbackList/GetArticleFeedbackListRequestValidator.cs \
        backend/src/Anela.Heblo.Application/Features/Article/ArticleModule.cs \
        backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs \
        backend/src/Anela.Heblo.Application/Features/Article/UseCases/GetFeedbackList/GetArticleFeedbackListHandler.cs \
        backend/test/Anela.Heblo.Tests/Article/UseCases/GetArticleFeedbackListHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Features/Article/Pipeline/ArticleFeedbackListValidationPipelineTests.cs
git commit -m "feat(article): validate GetArticleFeedbackList paging/sort at the API boundary"
```

---

