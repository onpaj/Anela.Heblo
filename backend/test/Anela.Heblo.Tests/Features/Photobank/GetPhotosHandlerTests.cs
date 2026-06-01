using System;
using System.Collections.Generic;
using System.Threading;
using Anela.Heblo.Application.Features.Photobank.UseCases.GetPhotos;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Photobank;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public class GetPhotosHandlerTests
{
    private readonly Mock<IPhotobankRepository> _repositoryMock;
    private readonly GetPhotosHandler _handler;

    public GetPhotosHandlerTests()
    {
        _repositoryMock = new Mock<IPhotobankRepository>();
        _handler = new GetPhotosHandler(_repositoryMock.Object);
    }

    private static Photo BuildPhoto(int id, string fileName, string folderPath, List<PhotoTag>? tags = null) =>
        new()
        {
            Id = id,
            SharePointFileId = $"sp-{id}",
            FileName = fileName,
            FolderPath = folderPath,
            SharePointWebUrl = $"https://sp.example.com/file-{id}",
            ModifiedAt = DateTime.UtcNow,
            Tags = tags ?? new List<PhotoTag>(),
        };

    private static Tag BuildTag(int id, string name) => new() { Id = id, Name = name };

    [Fact]
    public async System.Threading.Tasks.Task Handle_NoFilters_ReturnsAllPhotos()
    {
        // Arrange
        var photos = new List<Photo>
        {
            BuildPhoto(1, "photo1.jpg", "Photos/2025"),
            BuildPhoto(2, "photo2.jpg", "Photos/2026"),
        };

        _repositoryMock
            .Setup(r => r.GetPhotosAsync(null, null, false, false, 1, 48, It.IsAny<CancellationToken>()))
            .ReturnsAsync((photos, 2));

        var request = new GetPhotosRequest();

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Total.Should().Be(2);
        result.Items.Should().HaveCount(2);
        result.Items[0].Id.Should().Be(1);
        result.Items[1].Id.Should().Be(2);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(48);
    }

    [Fact]
    public async System.Threading.Tasks.Task Handle_FilterByTag_PassesTagsToRepository()
    {
        // Arrange
        var tag = BuildTag(1, "products");
        var photoTag = new PhotoTag { PhotoId = 1, TagId = 1, Source = PhotoTagSource.Rule, Tag = tag };
        var photos = new List<Photo>
        {
            BuildPhoto(1, "product.jpg", "Photos/Products", new List<PhotoTag> { photoTag }),
        };
        var tagFilter = new List<string> { "products" };

        _repositoryMock
            .Setup(r => r.GetPhotosAsync(tagFilter, null, false, false, 1, 48, It.IsAny<CancellationToken>()))
            .ReturnsAsync((photos, 1));

        var request = new GetPhotosRequest { Tags = tagFilter };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Total.Should().Be(1);
        result.Items.Should().HaveCount(1);
        result.Items[0].Tags.Should().ContainSingle(t => t.Name == "products");

        _repositoryMock.Verify(r => r.GetPhotosAsync(tagFilter, null, false, false, 1, 48, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async System.Threading.Tasks.Task Handle_FilterBySearch_PassesSearchToRepository()
    {
        // Arrange
        var photos = new List<Photo>
        {
            BuildPhoto(1, "ruze-cervena.jpg", "Photos/Products"),
        };

        _repositoryMock
            .Setup(r => r.GetPhotosAsync(null, "ruze", false, false, 1, 48, It.IsAny<CancellationToken>()))
            .ReturnsAsync((photos, 1));

        var request = new GetPhotosRequest { Search = "ruze" };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Items.Should().HaveCount(1);
        result.Items[0].Name.Should().Be("ruze-cervena.jpg");
    }

    [Fact]
    public async System.Threading.Tasks.Task Handle_EmptyResult_ReturnsEmptyList()
    {
        // Arrange
        _repositoryMock
            .Setup(r => r.GetPhotosAsync(It.IsAny<List<string>?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<bool>(), 1, 48, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Photo>(), 0));

        var request = new GetPhotosRequest { Tags = new List<string> { "nonexistent" } };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Total.Should().Be(0);
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async System.Threading.Tasks.Task Handle_UseRegexTrue_ForwardsUseRegexToRepository()
    {
        // Arrange
        var photos = new List<Photo>
        {
            BuildPhoto(1, "report_2024.pdf", "Reports"),
        };

        _repositoryMock
            .Setup(r => r.GetPhotosAsync(null, @"^report_\d+", true, false, 1, 48, It.IsAny<CancellationToken>()))
            .ReturnsAsync((photos, 1));

        var request = new GetPhotosRequest { Search = @"^report_\d+", UseRegex = true };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Items.Should().ContainSingle(p => p.Name == "report_2024.pdf");

        _repositoryMock.Verify(r => r.GetPhotosAsync(null, @"^report_\d+", true, false, 1, 48, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async System.Threading.Tasks.Task Handle_InvalidRegexPattern_ReturnsStructuredError()
    {
        // Arrange
        const string invalidPattern = "(unclosed";

        _repositoryMock
            .Setup(r => r.GetPhotosAsync(null, invalidPattern, true, false, 1, 48, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidPhotoSearchPatternException(invalidPattern));

        var request = new GetPhotosRequest { Search = invalidPattern, UseRegex = true };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.PhotobankInvalidRegexPattern);
        result.Params.Should().ContainKey("pattern");
        result.Params!["pattern"].Should().Be(invalidPattern);
    }
}
