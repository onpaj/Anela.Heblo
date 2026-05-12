using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Application.Features.Photobank.UseCases.BulkAddPhotoTagByIds;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Photobank;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public class BulkAddPhotoTagByIdsHandlerTests
{
    private readonly Mock<IPhotobankRepository> _repositoryMock;
    private readonly Mock<IPhotobankTagsCache> _cacheMock = new();
    private readonly BulkAddPhotoTagByIdsHandler _handler;

    public BulkAddPhotoTagByIdsHandlerTests()
    {
        _repositoryMock = new Mock<IPhotobankRepository>();
        _handler = new BulkAddPhotoTagByIdsHandler(_repositoryMock.Object, _cacheMock.Object);
    }

    private static Tag BuildTag(int id, string name) => new() { Id = id, Name = name };

    [Fact]
    public async Task Handle_EmptyPhotoIds_ReturnsInvalidRequestError()
    {
        // Arrange
        var request = new BulkAddPhotoTagByIdsRequest
        {
            PhotoIds = new List<int>(),
            TagName = "flowers",
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.BulkTagInvalidRequest);

        _repositoryMock.Verify(r => r.GetOrCreateTagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NullPhotoIds_ReturnsInvalidRequestError()
    {
        // Arrange
        var request = new BulkAddPhotoTagByIdsRequest
        {
            PhotoIds = null!,
            TagName = "flowers",
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.BulkTagInvalidRequest);

        _repositoryMock.Verify(r => r.GetOrCreateTagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_PhotoIdsExceedLimit_ReturnsBulkTagLimitExceededError()
    {
        // Arrange
        var photoIds = Enumerable.Range(1, 5_001).ToList();
        var request = new BulkAddPhotoTagByIdsRequest
        {
            PhotoIds = photoIds,
            TagName = "flowers",
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.BulkTagLimitExceeded);
        result.Params.Should().ContainKey("Count").WhoseValue.Should().Be("5001");
        result.Params.Should().ContainKey("Limit").WhoseValue.Should().Be("5000");

        _repositoryMock.Verify(r => r.GetOrCreateTagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_GetOrCreateTagReturnsNull_ReturnsPhotoTagCreationFailedError()
    {
        // Arrange
        _repositoryMock
            .Setup(r => r.GetOrCreateTagAsync("flowers", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tag?)null);

        var request = new BulkAddPhotoTagByIdsRequest
        {
            PhotoIds = new List<int> { 1, 2, 3 },
            TagName = "flowers",
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.PhotoTagCreationFailed);

        _repositoryMock.Verify(r => r.AddPhotoTagAsync(It.IsAny<PhotoTag>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_HappyPath_AddsTagsAndReturnsCounts()
    {
        // Arrange
        var tag = BuildTag(7, "flowers");
        var photoIds = new List<int> { 1, 2, 3, 4, 5 };
        var missingTagIds = new List<int> { 1, 2, 3 };

        _repositoryMock
            .Setup(r => r.GetOrCreateTagAsync("flowers", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tag);

        _repositoryMock
            .Setup(r => r.GetExistingPhotoIdsMissingTagAsync(
                It.IsAny<IReadOnlyList<int>>(), 7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(missingTagIds);

        _repositoryMock
            .Setup(r => r.CountExistingPhotosAsync(
                It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(5); // all 5 IDs exist in the database

        _repositoryMock
            .Setup(r => r.AddPhotoTagAsync(It.IsAny<PhotoTag>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repositoryMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new BulkAddPhotoTagByIdsRequest
        {
            PhotoIds = photoIds,
            TagName = "  Flowers  ",
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.TagId.Should().Be(7);
        result.TagName.Should().Be("flowers");
        result.AddedCount.Should().Be(3);
        result.AlreadyTaggedCount.Should().Be(2); // 5 existing - 3 added

        _repositoryMock.Verify(r => r.AddPhotoTagAsync(
            It.Is<PhotoTag>(pt => pt.TagId == 7 && pt.Source == PhotoTagSource.Manual),
            It.IsAny<CancellationToken>()), Times.Exactly(3));

        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        _repositoryMock.Verify(r => r.GetOrCreateTagAsync("flowers", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhitespaceOnlyTagName_ReturnsInvalidRequestError()
    {
        // Arrange
        var request = new BulkAddPhotoTagByIdsRequest
        {
            PhotoIds = new List<int> { 1, 2, 3 },
            TagName = "   ",
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.BulkTagInvalidRequest);

        _repositoryMock.Verify(r => r.GetOrCreateTagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AllAlreadyTagged_ReturnsZeroAddedCountAndDoesNotSave()
    {
        // Arrange
        var tag = BuildTag(3, "sale");
        var photoIds = new List<int> { 10, 11, 12 };

        _repositoryMock
            .Setup(r => r.GetOrCreateTagAsync("sale", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tag);

        _repositoryMock
            .Setup(r => r.GetExistingPhotoIdsMissingTagAsync(
                It.IsAny<IReadOnlyList<int>>(), 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<int>());

        _repositoryMock
            .Setup(r => r.CountExistingPhotosAsync(
                It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3); // all 3 IDs exist in the database

        var request = new BulkAddPhotoTagByIdsRequest
        {
            PhotoIds = photoIds,
            TagName = "sale",
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.AddedCount.Should().Be(0);
        result.AlreadyTaggedCount.Should().Be(3); // 3 existing - 0 added

        _repositoryMock.Verify(r => r.AddPhotoTagAsync(It.IsAny<PhotoTag>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_DuplicatePhotoIdsProvided_DeduplicatesBeforeRepositoryCalls()
    {
        // Arrange
        var tag = BuildTag(5, "new");
        var photoIds = new List<int> { 1, 1, 2, 2, 3 }; // 5 items, 3 distinct
        var missingTagIds = new List<int> { 1, 2 };
        var expectedDistinctIds = new List<int> { 1, 2, 3 };

        _repositoryMock
            .Setup(r => r.GetOrCreateTagAsync("new", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tag);

        _repositoryMock
            .Setup(r => r.GetExistingPhotoIdsMissingTagAsync(
                It.IsAny<IReadOnlyList<int>>(), 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(missingTagIds);

        _repositoryMock
            .Setup(r => r.CountExistingPhotosAsync(
                It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3); // all 3 distinct IDs exist in the database

        _repositoryMock
            .Setup(r => r.AddPhotoTagAsync(It.IsAny<PhotoTag>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repositoryMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new BulkAddPhotoTagByIdsRequest
        {
            PhotoIds = photoIds,
            TagName = "new",
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.AddedCount.Should().Be(2);
        result.AlreadyTaggedCount.Should().Be(1); // 3 existing - 2 added

        // Verify CountExistingPhotosAsync is called with the deduplicated list
        _repositoryMock.Verify(r => r.CountExistingPhotosAsync(
            It.Is<IReadOnlyList<int>>(ids => ids.SequenceEqual(expectedDistinctIds)),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify GetExistingPhotoIdsMissingTagAsync is called with the deduplicated list
        _repositoryMock.Verify(r => r.GetExistingPhotoIdsMissingTagAsync(
            It.Is<IReadOnlyList<int>>(ids => ids.SequenceEqual(expectedDistinctIds)),
            5,
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
