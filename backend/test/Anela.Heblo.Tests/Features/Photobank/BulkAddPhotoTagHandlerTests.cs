using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank;
using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Application.Features.Photobank.UseCases.BulkAddPhotoTag;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Photobank;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public class BulkAddPhotoTagHandlerTests
{
    private readonly Mock<IPhotobankRepository> _repositoryMock;
    private readonly Mock<IPhotobankTagsCache> _cacheMock = new();
    private readonly BulkAddPhotoTagHandler _handler;

    public BulkAddPhotoTagHandlerTests()
    {
        _repositoryMock = new Mock<IPhotobankRepository>();
        _handler = new BulkAddPhotoTagHandler(_repositoryMock.Object, _cacheMock.Object);
    }

    private static Tag BuildTag(int id, string name) => new() { Id = id, Name = name };

    [Fact]
    public async Task Handle_OnlySearchProvided_TagsAllPhotosAndReturnsSuccess()
    {
        // Arrange
        var tag = BuildTag(7, "flowers");
        var photoIds = new List<int> { 1, 2, 3, 4, 5 };

        _repositoryMock
            .Setup(r => r.CountFilteredPhotosAsync(null, "ruze", It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);

        _repositoryMock
            .Setup(r => r.GetOrCreateTagAsync("flowers", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tag);

        _repositoryMock
            .Setup(r => r.GetFilteredPhotoIdsMissingTagAsync(null, "ruze", 7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(photoIds);

        _repositoryMock
            .Setup(r => r.AddPhotoTagAsync(It.IsAny<PhotoTag>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repositoryMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new BulkAddPhotoTagRequest
        {
            TagName = "flowers",
            Search = "ruze",
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.TagId.Should().Be(7);
        result.TagName.Should().Be("flowers");
        result.AddedCount.Should().Be(5);
        result.AlreadyTaggedCount.Should().Be(0);

        _repositoryMock.Verify(r => r.AddPhotoTagAsync(
            It.Is<PhotoTag>(pt => pt.TagId == 7 && pt.Source == PhotoTagSource.Manual),
            It.IsAny<CancellationToken>()), Times.Exactly(5));

        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_CountExceedsLimit_ReturnsBulkTagLimitExceededError()
    {
        // Arrange
        _repositoryMock
            .Setup(r => r.CountFilteredPhotosAsync(null, "Photos", It.IsAny<CancellationToken>()))
            .ReturnsAsync(5_001);

        var request = new BulkAddPhotoTagRequest
        {
            TagName = "flowers",
            Search = "Photos",
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.BulkTagLimitExceeded);
        result.Params.Should().ContainKey("Count").WhoseValue.Should().Be("5001");
        result.Params.Should().ContainKey("Limit").WhoseValue.Should().Be(PhotobankConstants.BulkTagLimit.ToString());

        _repositoryMock.Verify(r => r.GetOrCreateTagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AllPhotosAlreadyTagged_ReturnsSuccessWithAddedCountZero()
    {
        // Arrange
        var tag = BuildTag(3, "sale");
        const int totalCount = 10;

        _repositoryMock
            .Setup(r => r.CountFilteredPhotosAsync(null, "produkt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(totalCount);

        _repositoryMock
            .Setup(r => r.GetOrCreateTagAsync("sale", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tag);

        _repositoryMock
            .Setup(r => r.GetFilteredPhotoIdsMissingTagAsync(null, "produkt", 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<int>());

        var request = new BulkAddPhotoTagRequest
        {
            TagName = "sale",
            Search = "produkt",
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.AddedCount.Should().Be(0);
        result.AlreadyTaggedCount.Should().Be(totalCount);

        _repositoryMock.Verify(r => r.AddPhotoTagAsync(It.IsAny<PhotoTag>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_MixedTagged_AddedCountPlusAlreadyTaggedEqualTotal()
    {
        // Arrange
        var tag = BuildTag(5, "new");
        const int totalCount = 8;

        _repositoryMock
            .Setup(r => r.CountFilteredPhotosAsync(It.IsAny<List<string>?>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(totalCount);

        _repositoryMock
            .Setup(r => r.GetOrCreateTagAsync("new", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tag);

        _repositoryMock
            .Setup(r => r.GetFilteredPhotoIdsMissingTagAsync(
                It.IsAny<List<string>?>(), null, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<int> { 10, 11, 12 });

        _repositoryMock
            .Setup(r => r.AddPhotoTagAsync(It.IsAny<PhotoTag>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repositoryMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new BulkAddPhotoTagRequest
        {
            TagName = "new",
            Tags = new List<string> { "summer" },
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.AddedCount.Should().Be(3);
        result.AlreadyTaggedCount.Should().Be(5);
        (result.AddedCount + result.AlreadyTaggedCount).Should().Be(totalCount);
    }

    [Fact]
    public async Task Handle_PhotoIdsEmpty_SaveChangesIsNotCalled()
    {
        // Arrange
        var tag = BuildTag(2, "archived");

        _repositoryMock
            .Setup(r => r.CountFilteredPhotosAsync(null, "Archive", It.IsAny<CancellationToken>()))
            .ReturnsAsync(4);

        _repositoryMock
            .Setup(r => r.GetOrCreateTagAsync("archived", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tag);

        _repositoryMock
            .Setup(r => r.GetFilteredPhotoIdsMissingTagAsync(null, "Archive", 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<int>());

        var request = new BulkAddPhotoTagRequest
        {
            TagName = "archived",
            Search = "Archive",
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
