using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using Anela.Heblo.Application.Features.Photobank.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Application.Features.Photobank.UseCases.RetagPhotos;
using Anela.Heblo.Domain.Features.Photobank;
using Anela.Heblo.Xcc.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public class RetagPhotosHandlerTests
{
    private readonly Mock<IPhotobankRepository> _repositoryMock;
    private readonly Mock<IBackgroundWorker> _backgroundWorkerMock;
    private readonly Mock<IPhotobankTagsCache> _cacheMock = new();
    private readonly RetagPhotosHandler _handler;

    public RetagPhotosHandlerTests()
    {
        _repositoryMock = new Mock<IPhotobankRepository>();
        _backgroundWorkerMock = new Mock<IBackgroundWorker>();
        _handler = new RetagPhotosHandler(_repositoryMock.Object, _backgroundWorkerMock.Object, _cacheMock.Object);
    }

    [Fact]
    public async System.Threading.Tasks.Task Handle_ResetsLastAutoTaggedAt_ForAllPhotoIds()
    {
        // Arrange
        var photoIds = new[] { 1, 2, 3 };
        var photos = photoIds
            .Select(id => new Photo { Id = id, FolderPath = "Photos", FileName = $"photo{id}.jpg" })
            .ToList();

        _repositoryMock
            .Setup(r => r.GetPhotosByIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(photos);

        _repositoryMock
            .Setup(r => r.ResetAutoTaggedAtAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .Returns(System.Threading.Tasks.Task.CompletedTask);

        _backgroundWorkerMock
            .Setup(w => w.Enqueue<PhotobankAutoTagJob>(It.IsAny<Expression<Func<PhotobankAutoTagJob, System.Threading.Tasks.Task>>>()))
            .Returns("job-123");

        var request = new RetagPhotosRequest { PhotoIds = photoIds, ClearExistingAiTags = false };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        _repositoryMock.Verify(
            r => r.ResetAutoTaggedAtAsync(
                It.Is<IReadOnlyList<int>>(ids => ids.Count == 3 && ids.Contains(1) && ids.Contains(2) && ids.Contains(3)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async System.Threading.Tasks.Task Handle_ClearsAiTags_WhenClearExistingAiTagsIsTrue()
    {
        // Arrange
        var photoIds = new[] { 10, 20 };
        var photos = photoIds
            .Select(id => new Photo { Id = id, FolderPath = "Photos", FileName = $"photo{id}.jpg" })
            .ToList();

        _repositoryMock
            .Setup(r => r.GetPhotosByIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(photos);

        _repositoryMock
            .Setup(r => r.ResetAutoTaggedAtAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .Returns(System.Threading.Tasks.Task.CompletedTask);

        _repositoryMock
            .Setup(r => r.RemovePhotoTagsBySourceAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<PhotoTagSource>(), It.IsAny<CancellationToken>()))
            .Returns(System.Threading.Tasks.Task.CompletedTask);

        _backgroundWorkerMock
            .Setup(w => w.Enqueue<PhotobankAutoTagJob>(It.IsAny<Expression<Func<PhotobankAutoTagJob, System.Threading.Tasks.Task>>>()))
            .Returns("job-456");

        var request = new RetagPhotosRequest { PhotoIds = photoIds, ClearExistingAiTags = true };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        _repositoryMock.Verify(
            r => r.RemovePhotoTagsBySourceAsync(
                It.IsAny<IReadOnlyList<int>>(),
                PhotoTagSource.AI,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async System.Threading.Tasks.Task Handle_ReturnsNullJobId_WhenNoPhotosFound()
    {
        // Arrange
        _repositoryMock
            .Setup(r => r.GetPhotosByIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Photo>());

        var request = new RetagPhotosRequest { PhotoIds = new[] { 999 } };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.JobId.Should().BeNull();

        _repositoryMock.Verify(r => r.ResetAutoTaggedAtAsync(
            It.IsAny<IReadOnlyList<int>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async System.Threading.Tasks.Task Handle_DoesNotClearAiTags_WhenClearExistingAiTagsIsFalse()
    {
        // Arrange
        var photoIds = new[] { 5 };
        var photos = photoIds
            .Select(id => new Photo { Id = id, FolderPath = "Photos", FileName = $"photo{id}.jpg" })
            .ToList();

        _repositoryMock
            .Setup(r => r.GetPhotosByIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(photos);

        _repositoryMock
            .Setup(r => r.ResetAutoTaggedAtAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .Returns(System.Threading.Tasks.Task.CompletedTask);

        _backgroundWorkerMock
            .Setup(w => w.Enqueue<PhotobankAutoTagJob>(It.IsAny<Expression<Func<PhotobankAutoTagJob, System.Threading.Tasks.Task>>>()))
            .Returns("job-789");

        var request = new RetagPhotosRequest { PhotoIds = photoIds, ClearExistingAiTags = false };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        _repositoryMock.Verify(
            r => r.RemovePhotoTagsBySourceAsync(
                It.IsAny<IReadOnlyList<int>>(),
                It.IsAny<PhotoTagSource>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
