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
