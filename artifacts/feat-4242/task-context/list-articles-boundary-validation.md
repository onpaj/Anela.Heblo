### task: list-articles-boundary-validation

**Title:** `ListArticles` boundary validation

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Article/UseCases/ListArticles/ListArticlesRequestValidator.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Article/Pipeline/ArticleListValidationPipelineTests.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Article/ArticleModule.cs`
- Modify: `backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs:55-69`
- Modify: `backend/src/Anela.Heblo.Application/Features/Article/UseCases/ListArticles/ListArticlesHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Article/UseCases/ListArticlesHandlerTests.cs`

- [ ] **Step 1: Write the failing pipeline test**

Create `backend/test/Anela.Heblo.Tests/Features/Article/Pipeline/ArticleListValidationPipelineTests.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.Article.UseCases.ListArticles;
using Anela.Heblo.Domain.Features.Article;
using FluentAssertions;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;
using DomainArticle = Anela.Heblo.Domain.Features.Article.Article;

namespace Anela.Heblo.Tests.Features.Article.Pipeline;

/// <summary>
/// Integration tests for the ListArticles validation pipeline behavior.
/// Verifies that ValidationResultBehavior + ListArticlesRequestValidator are wired
/// correctly (mirroring AnalyticsModule's/FileStorageModule's DI pattern), so an
/// invalid Page/PageSize short-circuits before ListArticlesHandler.Handle executes,
/// and a valid one reaches it unmodified.
/// </summary>
public class ArticleListValidationPipelineTests
{
    private static IMediator BuildMediator(Mock<IArticleRepository> repository)
    {
        var services = new ServiceCollection();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(ListArticlesHandler).Assembly));

        services.AddScoped<IValidator<ListArticlesRequest>, ListArticlesRequestValidator>();
        services.AddScoped<IPipelineBehavior<ListArticlesRequest, ListArticlesResponse>,
            ValidationResultBehavior<ListArticlesRequest, ListArticlesResponse>>();

        services.AddScoped(_ => repository.Object);

        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(-5, 20)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 101)]
    [InlineData(1, 1_000_000)]
    public async Task Send_InvalidPageOrPageSize_ShortCircuits_RepositoryNeverInvoked(int page, int pageSize)
    {
        // Arrange
        var repository = new Mock<IArticleRepository>();
        var mediator = BuildMediator(repository);

        var request = new ListArticlesRequest { Page = page, PageSize = pageSize };

        // Act
        var result = await mediator.Send(request);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(Anela.Heblo.Application.Shared.ErrorCodes.ValidationError);
        repository.Verify(
            r => r.GetPagedAsync(
                It.IsAny<ArticleStatus?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 20)]
    [InlineData(1, 100)]
    [InlineData(5, 50)]
    public async Task Send_ValidPageAndPageSize_ReachesHandler_ReturnsSuccess(int page, int pageSize)
    {
        // Arrange
        var repository = new Mock<IArticleRepository>();
        repository
            .Setup(r => r.GetPagedAsync(
                It.IsAny<ArticleStatus?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<DomainArticle>(), 0));
        var mediator = BuildMediator(repository);

        var request = new ListArticlesRequest { Page = page, PageSize = pageSize };

        // Act
        var result = await mediator.Send(request);

        // Assert
        result.Success.Should().BeTrue();
        result.Page.Should().Be(page);
        result.PageSize.Should().Be(pageSize);
        repository.Verify(
            r => r.GetPagedAsync(It.IsAny<ArticleStatus?>(), page, pageSize, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails to compile (validator does not exist yet)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~ArticleListValidationPipelineTests`
Expected: FAIL — build error, `ListArticlesRequestValidator` does not exist.

- [ ] **Step 3: Create the validator**

Create `backend/src/Anela.Heblo.Application/Features/Article/UseCases/ListArticles/ListArticlesRequestValidator.cs`:

```csharp
using FluentValidation;

namespace Anela.Heblo.Application.Features.Article.UseCases.ListArticles;

public class ListArticlesRequestValidator : AbstractValidator<ListArticlesRequest>
{
    public ListArticlesRequestValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}
```

- [ ] **Step 4: Run the test to verify it now compiles and passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~ArticleListValidationPipelineTests`
Expected: PASS (all 10 cases across both `[Theory]` methods).

- [ ] **Step 5: Register the validator and pipeline behavior in `ArticleModule`**

Open `backend/src/Anela.Heblo.Application/Features/Article/ArticleModule.cs` and add the imports and registrations:

```csharp
using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.Article.UseCases.Generate;
using Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;
using Anela.Heblo.Application.Features.Article.UseCases.ListArticles;
using Anela.Heblo.Domain.Features.Article;
using Anela.Heblo.Persistence.Features.Article;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Article;

public static class ArticleModule
{
    public static IServiceCollection AddArticleModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<ArticleOptions>()
            .Bind(configuration.GetSection(ArticleOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Repositories (implementations live in the Persistence layer)
        services.AddScoped<IArticleRepository, ArticleRepository>();
        services.AddScoped<IArticleAdminRepository, ArticleAdminRepository>();

        services.AddScoped<PipelineStepRecorder>();
        services.AddScoped<IPlanQueriesStep, PlanQueriesStep>();
        services.AddScoped<IGatherContextStep, GatherContextStep>();
        services.AddScoped<IAggregateFactsStep, AggregateFactsStep>();
        services.AddScoped<IValidateFactsStep, ValidateFactsStep>();
        services.AddScoped<IWriteArticleStep, WriteArticleStep>();
        services.AddScoped<GenerateArticleJob>();

        services.AddScoped<IValidator<ListArticlesRequest>, ListArticlesRequestValidator>();
        services.AddScoped<
            IPipelineBehavior<ListArticlesRequest, ListArticlesResponse>,
            ValidationResultBehavior<ListArticlesRequest, ListArticlesResponse>>();

        return services;
    }
}
```

(Only the `using Anela.Heblo.Application.Common.Behaviors;`, `using Anela.Heblo.Application.Features.Article.UseCases.ListArticles;`, `using FluentValidation;`, `using MediatR;` usings and the final two `services.AddScoped` calls are new — everything else in the file is unchanged.)

- [ ] **Step 6: Bind the controller action directly to `ListArticlesRequest`**

In `backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs`, replace lines 55-69:

```csharp
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
```

with:

```csharp
    [HttpGet]
    public async Task<ActionResult<ListArticlesResponse>> List(
        [FromQuery] ListArticlesRequest request,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(request, ct);
        return HandleResponse(result);
    }
```

`ArticleStatus` (from `using Anela.Heblo.Domain.Features.Article;` at the top of the file) was only referenced in this action's `status` parameter — confirmed by `grep -n "ArticleStatus\|Domain.Features.Article" backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs` showing exactly two lines: the `using` and the now-deleted parameter. Remove that `using Anela.Heblo.Domain.Features.Article;` line from the top of the file — it is unused after this step.

- [ ] **Step 7: Remove the clamping code from `ListArticlesHandler`**

In `backend/src/Anela.Heblo.Application/Features/Article/UseCases/ListArticles/ListArticlesHandler.cs`, replace:

```csharp
    public async Task<ListArticlesResponse> Handle(
        ListArticlesRequest request,
        CancellationToken cancellationToken)
    {
        // Clamped here because the controller manually constructs the request instead of
        // binding it, so ASP.NET Core model validation never runs for this endpoint.
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var (items, totalCount) = await _repository.GetPagedAsync(
            request.Status,
            page,
            pageSize,
            cancellationToken);

        return new ListArticlesResponse
        {
            Items = items.Select(a => new ArticleListItemDto
            {
                Id = a.Id,
                Topic = a.Topic,
                Title = a.Title,
                Status = a.Status,
                CreatedAt = a.CreatedAt,
                GeneratedAt = a.GeneratedAt
            }).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }
```

with:

```csharp
    public async Task<ListArticlesResponse> Handle(
        ListArticlesRequest request,
        CancellationToken cancellationToken)
    {
        var (items, totalCount) = await _repository.GetPagedAsync(
            request.Status,
            request.Page,
            request.PageSize,
            cancellationToken);

        return new ListArticlesResponse
        {
            Items = items.Select(a => new ArticleListItemDto
            {
                Id = a.Id,
                Topic = a.Topic,
                Title = a.Title,
                Status = a.Status,
                CreatedAt = a.CreatedAt,
                GeneratedAt = a.GeneratedAt
            }).ToList(),
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize
        };
    }
```

- [ ] **Step 8: Replace the clamp-assertion handler tests with plain pass-through tests**

In `backend/test/Anela.Heblo.Tests/Article/UseCases/ListArticlesHandlerTests.cs`, delete these three test methods entirely (their asserted behavior — the handler self-correcting invalid input — no longer exists; it is now covered by `ArticleListValidationPipelineTests` from Step 1, which asserts invalid input never reaches the handler at all):
- `Handle_ClampsOversizedPageSizeTo100`
- `Handle_ClampsNonPositivePageSizeTo1`
- `Handle_ClampsNonPositivePageTo1`

Keep `Handle_ReturnsMappedListWithPaginationInfo` and `Handle_PassesStatusFilterThroughToRepository` unchanged — they already only use valid `Page`/`PageSize` values and assert the handler passes them through, which remains correct.

- [ ] **Step 9: Run the full Article test suite and build to verify everything passes**

Run: `dotnet build Anela.Heblo.sln`
Expected: Build succeeds, no unused-using warnings escalated to errors.

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ListArticlesHandlerTests|FullyQualifiedName~ArticleListValidationPipelineTests"`
Expected: PASS — `ListArticlesHandlerTests` (2 remaining tests) and `ArticleListValidationPipelineTests` (10 cases) all green.

- [ ] **Step 10: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Article/UseCases/ListArticles/ListArticlesRequestValidator.cs \
        backend/src/Anela.Heblo.Application/Features/Article/ArticleModule.cs \
        backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs \
        backend/src/Anela.Heblo.Application/Features/Article/UseCases/ListArticles/ListArticlesHandler.cs \
        backend/test/Anela.Heblo.Tests/Article/UseCases/ListArticlesHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Features/Article/Pipeline/ArticleListValidationPipelineTests.cs
git commit -m "feat(article): validate ListArticles paging at the API boundary"
```

---

