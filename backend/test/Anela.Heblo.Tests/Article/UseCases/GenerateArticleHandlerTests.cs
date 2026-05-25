using Anela.Heblo.Application.Features.Article.UseCases.Generate;
using Anela.Heblo.Application.Features.Article.UseCases.GenerateArticle;
using Anela.Heblo.Domain.Features.Article;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Moq;
using DomainArticle = Anela.Heblo.Domain.Features.Article.Article;

namespace Anela.Heblo.Tests.Article.UseCases;

public class GenerateArticleHandlerTests
{
    private readonly Mock<IArticleRepository> _repository = new();
    private readonly Mock<IBackgroundJobClient> _backgroundJobClient = new();
    private readonly Mock<ICurrentUserService> _currentUserService = new();

    private GenerateArticleHandler CreateHandler() =>
        new(_repository.Object, _backgroundJobClient.Object, _currentUserService.Object);

    private void SetupAuthenticatedUser(string name = "Test User") =>
        _currentUserService
            .Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser("user-id", name, "test@example.com", IsAuthenticated: true));

    private void SetupAnonymousUser() =>
        _currentUserService
            .Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser(null, null, null, IsAuthenticated: false));

    [Fact]
    public async Task Handle_HappyPath_CreatesArticleWithMappedFields()
    {
        SetupAuthenticatedUser("John Doe");
        DomainArticle? captured = null;
        _repository
            .Setup(r => r.AddAsync(It.IsAny<DomainArticle>(), It.IsAny<CancellationToken>()))
            .Callback<DomainArticle, CancellationToken>((a, _) => captured = a)
            .Returns(Task.CompletedTask);

        var request = new GenerateArticleRequest
        {
            Topic = "Sun protection in winter",
            Scope = "deep-dive",
            Audience = "professionals",
            Angle = "scientific",
            Length = "long (2000w)",
            LanguageNote = "use Czech",
            UseKnowledgeBase = true,
            UseWebSearch = false,
            StyleGuideDriveId = "drive-1",
            StyleGuideItemPath = "/styles/guide.docx"
        };

        var response = await CreateHandler().Handle(request, default);

        response.ArticleId.Should().NotBeNull().And.NotBe(Guid.Empty);
        captured.Should().NotBeNull();
        captured!.Id.Should().Be(response.ArticleId!.Value);
        captured.Topic.Should().Be(request.Topic);
        captured.Scope.Should().Be(request.Scope);
        captured.Audience.Should().Be(request.Audience);
        captured.Angle.Should().Be(request.Angle);
        captured.Length.Should().Be(request.Length);
        captured.LanguageNote.Should().Be(request.LanguageNote);
        captured.UsedKnowledgeBase.Should().BeTrue();
        captured.UsedWebSearch.Should().BeFalse();
        captured.StyleGuideDriveId.Should().Be(request.StyleGuideDriveId);
        captured.StyleGuideItemPath.Should().Be(request.StyleGuideItemPath);
        captured.Status.Should().Be(ArticleStatus.Queued);
        captured.RequestedBy.Should().Be("John Doe");
    }

    [Fact]
    public async Task Handle_AnonymousUser_RequestedByIsNull()
    {
        SetupAnonymousUser();
        DomainArticle? captured = null;
        _repository
            .Setup(r => r.AddAsync(It.IsAny<DomainArticle>(), It.IsAny<CancellationToken>()))
            .Callback<DomainArticle, CancellationToken>((a, _) => captured = a)
            .Returns(Task.CompletedTask);

        await CreateHandler().Handle(new GenerateArticleRequest { Topic = "Topic" }, default);

        captured!.RequestedBy.Should().BeNull();
    }

    [Fact]
    public async Task Handle_PersistsAndEnqueuesHangfireJob()
    {
        SetupAuthenticatedUser();

        await CreateHandler().Handle(new GenerateArticleRequest { Topic = "Topic" }, default);

        _repository.Verify(r => r.AddAsync(It.IsAny<DomainArticle>(), It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _backgroundJobClient.Verify(c => c.Create(
            It.Is<Job>(j => j.Type == typeof(GenerateArticleJob) && j.Method.Name == nameof(GenerateArticleJob.RunAsync)),
            It.IsAny<EnqueuedState>()), Times.Once);
    }

    [Fact]
    public async Task Handle_HappyPath_ReturnsHangfireJobIdAndQueuedStatus()
    {
        SetupAuthenticatedUser();
        _backgroundJobClient
            .Setup(c => c.Create(It.IsAny<Job>(), It.IsAny<EnqueuedState>()))
            .Returns("job-123");

        var response = await CreateHandler().Handle(new GenerateArticleRequest { Topic = "Topic" }, default);

        response.Success.Should().BeTrue();
        response.HangfireJobId.Should().Be("job-123");
        response.Status.Should().Be(ArticleStatus.Queued);
        response.ArticleId.Should().NotBeNull().And.NotBe(Guid.Empty);
    }
}
