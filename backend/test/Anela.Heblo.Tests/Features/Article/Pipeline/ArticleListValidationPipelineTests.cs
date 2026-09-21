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
