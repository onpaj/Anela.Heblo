using System.Text.Json;
using Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;
using Anela.Heblo.Domain.Features.Article;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Article;

public class PipelineStepRecorderTests
{
    private readonly Mock<IArticleRepository> _repositoryMock = new();
    private readonly PipelineStepRecorder _recorder;

    public PipelineStepRecorderTests()
    {
        _recorder = new PipelineStepRecorder(_repositoryMock.Object);
    }

    [Fact]
    public async Task RecordAsync_AddsRunningStep_ThenSucceededStep_OnSuccess()
    {
        ArticleGenerationStepStatus? addedStatus = null;
        string? addedStepName = null;
        int? addedSequence = null;
        string? addedModel = null;
        ArticleGenerationStep? updatedStep = null;

        _repositoryMock
            .Setup(r => r.AddStepAsync(It.IsAny<ArticleGenerationStep>(), It.IsAny<CancellationToken>()))
            .Callback<ArticleGenerationStep, CancellationToken>((s, _) =>
            {
                addedStatus = s.Status;
                addedStepName = s.StepName;
                addedSequence = s.Sequence;
                addedModel = s.Model;
            })
            .Returns(Task.CompletedTask);

        _repositoryMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repositoryMock
            .Setup(r => r.UpdateStepAsync(It.IsAny<ArticleGenerationStep>(), It.IsAny<CancellationToken>()))
            .Callback<ArticleGenerationStep, CancellationToken>((s, _) => updatedStep = s)
            .Returns(Task.CompletedTask);

        var result = await _recorder.RecordAsync<string>(
            Guid.NewGuid(), "PlanQueries", 1, "gpt-4o",
            new { query = "test" },
            async (ct) =>
            {
                await Task.Yield();
                return ("hello", (object?)new { result = "world" });
            },
            CancellationToken.None);

        result.Should().Be("hello");

        addedStatus.Should().Be(ArticleGenerationStepStatus.Running);
        addedStepName.Should().Be("PlanQueries");
        addedSequence.Should().Be(1);
        addedModel.Should().Be("gpt-4o");

        updatedStep.Should().NotBeNull();
        updatedStep!.Status.Should().Be(ArticleGenerationStepStatus.Succeeded);
        updatedStep.FinishedAt.Should().NotBeNull();
        updatedStep.DurationMs.Should().BeGreaterThanOrEqualTo(0);
        updatedStep.OutputJson.Should().NotBeNullOrEmpty();

        _repositoryMock.Verify(r => r.AddStepAsync(It.IsAny<ArticleGenerationStep>(), It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.UpdateStepAsync(It.IsAny<ArticleGenerationStep>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordAsync_MarksStepFailed_AndRethrows_OnException()
    {
        ArticleGenerationStep? updatedStep = null;

        _repositoryMock
            .Setup(r => r.AddStepAsync(It.IsAny<ArticleGenerationStep>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repositoryMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repositoryMock
            .Setup(r => r.UpdateStepAsync(It.IsAny<ArticleGenerationStep>(), It.IsAny<CancellationToken>()))
            .Callback<ArticleGenerationStep, CancellationToken>((s, _) => updatedStep = s)
            .Returns(Task.CompletedTask);

        var act = async () => await _recorder.RecordAsync<string>(
            Guid.NewGuid(), "WriteArticle", 5, null,
            null,
            async (ct) =>
            {
                await Task.Yield();
                throw new InvalidOperationException("boom");
#pragma warning disable CS0162
                return ("", (object?)null);
#pragma warning restore CS0162
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");

        updatedStep.Should().NotBeNull();
        updatedStep!.Status.Should().Be(ArticleGenerationStepStatus.Failed);
        updatedStep.ErrorMessage.Should().Be("boom");
        updatedStep.FinishedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RecordAsync_SerializesInputAndOutput()
    {
        ArticleGenerationStep? addedStep = null;
        ArticleGenerationStep? updatedStep = null;

        _repositoryMock
            .Setup(r => r.AddStepAsync(It.IsAny<ArticleGenerationStep>(), It.IsAny<CancellationToken>()))
            .Callback<ArticleGenerationStep, CancellationToken>((s, _) => addedStep = s)
            .Returns(Task.CompletedTask);

        _repositoryMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repositoryMock
            .Setup(r => r.UpdateStepAsync(It.IsAny<ArticleGenerationStep>(), It.IsAny<CancellationToken>()))
            .Callback<ArticleGenerationStep, CancellationToken>((s, _) => updatedStep = s)
            .Returns(Task.CompletedTask);

        var input = new { query = "cosme" };
        var output = new { rawResponse = "abc", queries = new[] { "q1" } };

        await _recorder.RecordAsync<int>(
            Guid.NewGuid(), "PlanQueries", 1, null,
            input,
            async (ct) => { await Task.Yield(); return (42, (object?)output); },
            CancellationToken.None);

        addedStep!.InputJson.Should().NotBeNull();
        var parsedInput = JsonSerializer.Deserialize<JsonElement>(addedStep!.InputJson!);
        parsedInput.GetProperty("query").GetString().Should().Be("cosme");

        updatedStep!.OutputJson.Should().NotBeNull();
        var parsedOutput = JsonSerializer.Deserialize<JsonElement>(updatedStep!.OutputJson!);
        parsedOutput.GetProperty("rawResponse").GetString().Should().Be("abc");
    }
}
