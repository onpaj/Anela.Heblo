using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank;
using Anela.Heblo.Application.Features.Photobank.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Photobank;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Features.Photobank;

public class PhotobankAutoTagJobTests
{
    private readonly Mock<IPhotobankRepository> _repo = new();
    private readonly Mock<IChatClient> _chat = new();
    private readonly Mock<IPhotobankTagsCache> _cache = new();
    private readonly Mock<IRecurringJobStatusChecker> _statusChecker = new();

    public PhotobankAutoTagJobTests()
    {
        // Default: job is enabled. Individual tests override as needed.
        _statusChecker
            .Setup(s => s.IsJobEnabledAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()))
            .ReturnsAsync(true);
    }

    private PhotobankAutoTagJob CreateJob(AutoTagOptions? options = null)
    {
        var opts = options ?? new AutoTagOptions { BatchSize = 50, MaxPhotosPerRun = 5_000 };
        return new PhotobankAutoTagJob(
            _repo.Object,
            _chat.Object,
            Options.Create(opts),
            NullLogger<PhotobankAutoTagJob>.Instance,
            _cache.Object,
            _statusChecker.Object);
    }

    private void SetupEmptyTags() =>
        _repo
            .Setup(r => r.GetTagsWithCountsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TagCount>());

    private void SetupNoPendingPhotos() =>
        _repo
            .Setup(r => r.GetPhotosPendingAutoTagAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PhotoAutoTagCandidate>());

    private void SetupChatResponse(string json) =>
        _chat
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, json)]));

    [Fact]
    public async Task ExecuteAsync_WhenStatusCheckerReturnsFalse_DoesNotCallLlmOrRepository()
    {
        // Arrange
        _statusChecker
            .Setup(s => s.IsJobEnabledAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()))
            .ReturnsAsync(false);
        var job = CreateJob();

        // Act
        await job.ExecuteAsync(CancellationToken.None);

        // Assert
        _chat.Verify(
            c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _repo.Verify(
            r => r.GetTagsWithCountsAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenNoPendingPhotos_DoesNotCallLlm()
    {
        // Arrange
        SetupEmptyTags();
        SetupNoPendingPhotos();
        var job = CreateJob();

        // Act
        await job.ExecuteAsync(CancellationToken.None);

        // Assert
        _chat.Verify(
            c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_StampsAllPhotosInBatch_EvenWhenLlmReturnsEmptyTags()
    {
        // Arrange
        var candidates = new List<PhotoAutoTagCandidate>
        {
            new(Id: 1, FolderPath: "/photos", FileName: "a.jpg"),
            new(Id: 2, FolderPath: "/photos", FileName: "b.jpg"),
        };

        var tags = new List<TagCount>
        {
            new(10, "kosmetika", 5),
        };

        _repo
            .Setup(r => r.GetTagsWithCountsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(tags);

        _repo
            .SetupSequence(r => r.GetPhotosPendingAutoTagAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidates)
            .ReturnsAsync(new List<PhotoAutoTagCandidate>());

        SetupChatResponse("""{"results":[]}""");

        IReadOnlyList<int>? stampedIds = null;
        _repo
            .Setup(r => r.StampAutoTaggedAtAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<int>, DateTime, CancellationToken>((ids, _, _) => stampedIds = ids)
            .Returns(Task.CompletedTask);

        var job = CreateJob();

        // Act
        await job.ExecuteAsync(CancellationToken.None);

        // Assert
        stampedIds.Should().NotBeNull();
        stampedIds.Should().BeEquivalentTo(new[] { 1, 2 });
    }

    [Fact]
    public async Task ExecuteAsync_RespectsMaxTagsPerPhoto_Cap()
    {
        // Arrange
        var photos = new List<PhotoAutoTagCandidate>
        {
            new(20, "marketing", "photo.jpg"),
        };

        var tags = new List<TagCount>
        {
            new(1, "andy", 1),
            new(2, "ela", 1),
            new(3, "peťa", 1),
        };

        _repo
            .Setup(r => r.GetTagsWithCountsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(tags);

        _repo
            .SetupSequence(r => r.GetPhotosPendingAutoTagAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(photos)
            .ReturnsAsync(new List<PhotoAutoTagCandidate>());

        // LLM returns all 3 valid tags, but MaxTagsPerPhoto = 2 in the test options
        _chat
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                "{\"results\":[{\"id\":20,\"tags\":[\"andy\",\"ela\",\"peťa\"]}]}")));

        _repo.Setup(r => r.PhotoTagExistsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _repo
            .Setup(r => r.AddPhotoTagAsync(It.IsAny<PhotoTag>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repo
            .Setup(r => r.StampAutoTaggedAtAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Use job with MaxTagsPerPhoto = 2
        var jobWithCap = new PhotobankAutoTagJob(
            _repo.Object,
            _chat.Object,
            Options.Create(new AutoTagOptions { BatchSize = 50, MaxPhotosPerRun = 100, Model = "test-model", MaxTagsPerPhoto = 2 }),
            NullLogger<PhotobankAutoTagJob>.Instance,
            _cache.Object,
            _statusChecker.Object);

        // Act
        await jobWithCap.ExecuteAsync(CancellationToken.None);

        // Assert — only 2 out of 3 valid tags should be applied
        _repo.Verify(r => r.AddPhotoTagAsync(
            It.Is<PhotoTag>(pt => pt.PhotoId == 20 && pt.Source == PhotoTagSource.AI),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ExecuteAsync_AppliesValidTagsAndDropsHallucinations()
    {
        // Arrange
        var kandidat = new PhotoAutoTagCandidate(Id: 42, FolderPath: "/photos", FileName: "product.jpg");

        var tags = new List<TagCount>
        {
            new(1, "kosmetika", 3),
            new(2, "pleťová péče", 2),
        };

        _repo
            .Setup(r => r.GetTagsWithCountsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(tags);

        _repo
            .SetupSequence(r => r.GetPhotosPendingAutoTagAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PhotoAutoTagCandidate> { kandidat })
            .ReturnsAsync(new List<PhotoAutoTagCandidate>());

        SetupChatResponse("""
            {
              "results": [
                {
                  "id": 42,
                  "tags": ["kosmetika", "pleťová péče", "hallucinated-tag"]
                }
              ]
            }
            """);

        _repo
            .Setup(r => r.PhotoTagExistsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _repo
            .Setup(r => r.AddPhotoTagAsync(It.IsAny<PhotoTag>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repo
            .Setup(r => r.StampAutoTaggedAtAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var job = CreateJob();

        // Act
        await job.ExecuteAsync(CancellationToken.None);

        // Assert — only the two vocabulary tags are added, not the hallucination
        _repo.Verify(
            r => r.AddPhotoTagAsync(
                It.Is<PhotoTag>(pt => pt.PhotoId == 42 && pt.TagId == 1 && pt.Source == PhotoTagSource.AI),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _repo.Verify(
            r => r.AddPhotoTagAsync(
                It.Is<PhotoTag>(pt => pt.PhotoId == 42 && pt.TagId == 2 && pt.Source == PhotoTagSource.AI),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _repo.Verify(
            r => r.AddPhotoTagAsync(
                It.Is<PhotoTag>(pt => pt.PhotoId == 42 && pt.TagId == 99),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _repo.Verify(r => r.AddPhotoTagAsync(It.IsAny<PhotoTag>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ExecuteForPhotosAsync_RunsEvenWhenStatusCheckerReturnsFalse()
    {
        // Arrange — recurring-schedule toggle is OFF, but ad-hoc retag must still run.
        _statusChecker
            .Setup(s => s.IsJobEnabledAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()))
            .ReturnsAsync(false);

        var candidates = new List<PhotoAutoTagCandidate>
        {
            new(Id: 7, FolderPath: "/photos", FileName: "ad-hoc.jpg"),
        };

        _repo
            .Setup(r => r.GetTagsWithCountsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TagCount> { new(10, "kosmetika", 1) });

        SetupChatResponse("""{"results":[{"id":7,"tags":["kosmetika"]}]}""");

        _repo
            .Setup(r => r.PhotoTagExistsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _repo
            .Setup(r => r.AddPhotoTagAsync(It.IsAny<PhotoTag>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repo
            .Setup(r => r.StampAutoTaggedAtAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var job = CreateJob();

        // Act
        await job.ExecuteForPhotosAsync(candidates, CancellationToken.None);

        // Assert — LLM was invoked and the candidate was stamped, despite the recurring toggle being off.
        _chat.Verify(
            c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _repo.Verify(
            r => r.StampAutoTaggedAtAsync(
                It.Is<IReadOnlyList<int>>(ids => ids.Count == 1 && ids[0] == 7),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _statusChecker.Verify(
            s => s.IsJobEnabledAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_LlmReturnsStringEncodedId_AppliesTagsSuccessfully()
    {
        // Arrange — LLM returns "id" as a quoted string ("42") instead of a bare integer (42).
        // This is the regression from issue #3405: JsonNumberHandling.AllowReadingFromString must be
        // set on AutoTagResult.Id so the deserialiser accepts both forms.
        var photo = new PhotoAutoTagCandidate(Id: 42, FolderPath: "/photos", FileName: "product.jpg");

        _repo
            .Setup(r => r.GetTagsWithCountsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TagCount> { new(1, "kosmetika", 3) });

        _repo
            .SetupSequence(r => r.GetPhotosPendingAutoTagAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PhotoAutoTagCandidate> { photo })
            .ReturnsAsync(new List<PhotoAutoTagCandidate>());

        // id returned as a quoted string — the failing form from production telemetry
        SetupChatResponse("""{"results":[{"id":"42","tags":["kosmetika"]}]}""");

        _repo.Setup(r => r.PhotoTagExistsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _repo.Setup(r => r.AddPhotoTagAsync(It.IsAny<PhotoTag>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repo.Setup(r => r.StampAutoTaggedAtAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var job = CreateJob();

        // Act
        await job.ExecuteAsync(CancellationToken.None);

        // Assert — tag must have been applied even though the id came back as a string
        _repo.Verify(
            r => r.AddPhotoTagAsync(
                It.Is<PhotoTag>(pt => pt.PhotoId == 42 && pt.TagId == 1 && pt.Source == PhotoTagSource.AI),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _repo.Verify(
            r => r.StampAutoTaggedAtAsync(
                It.Is<IReadOnlyList<int>>(ids => ids.Count == 1 && ids[0] == 42),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
