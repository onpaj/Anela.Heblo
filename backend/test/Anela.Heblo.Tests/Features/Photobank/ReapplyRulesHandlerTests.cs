using System.Collections.Generic;
using System.Threading;
using Anela.Heblo.Application.Features.Photobank.Contracts;
using Anela.Heblo.Application.Features.Photobank.UseCases.ReapplyRules;
using Anela.Heblo.Domain.Features.Photobank;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public class ReapplyRulesHandlerTests
{
    private readonly Mock<IPhotobankRepository> _repositoryMock;
    private readonly ReapplyRulesHandler _handler;

    public ReapplyRulesHandlerTests()
    {
        _repositoryMock = new Mock<IPhotobankRepository>();
        _handler = new ReapplyRulesHandler(_repositoryMock.Object);
    }

    [Fact]
    public async System.Threading.Tasks.Task Handle_ReturnsPhotosUpdatedCount()
    {
        // Arrange
        var rules = new List<TagRule>
        {
            new() { Id = 1, PathPattern = "Photos/Products", TagName = "products", IsActive = true, SortOrder = 0 },
        };

        _repositoryMock
            .Setup(r => r.GetRulesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(rules);

        _repositoryMock
            .Setup(r => r.ReapplyRulesAsync(rules, It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);

        _repositoryMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(System.Threading.Tasks.Task.CompletedTask);

        // Act
        var result = await _handler.Handle(new ReapplyRulesRequest(), CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.PhotosUpdated.Should().Be(5);

        _repositoryMock.Verify(r => r.ReapplyRulesAsync(rules, It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async System.Threading.Tasks.Task Handle_PreservesManualTags_DelegatedToRepository()
    {
        // Arrange — verify that ReapplyRulesAsync is called (repository handles preservation logic)
        var rules = new List<TagRule>
        {
            new() { Id = 1, PathPattern = "Photos/Events", TagName = "events", IsActive = true, SortOrder = 0 },
        };

        _repositoryMock
            .Setup(r => r.GetRulesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(rules);

        _repositoryMock
            .Setup(r => r.ReapplyRulesAsync(rules, It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        _repositoryMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(System.Threading.Tasks.Task.CompletedTask);

        // Act
        var result = await _handler.Handle(new ReapplyRulesRequest(), CancellationToken.None);

        // Assert
        result.PhotosUpdated.Should().Be(3);
        // Repository is responsible for only removing Rule-source tags, not Manual ones
        _repositoryMock.Verify(r => r.ReapplyRulesAsync(It.IsAny<List<TagRule>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async System.Threading.Tasks.Task Handle_NoRules_ReturnsZeroPhotosUpdated()
    {
        // Arrange
        _repositoryMock
            .Setup(r => r.GetRulesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TagRule>());

        _repositoryMock
            .Setup(r => r.ReapplyRulesAsync(It.IsAny<List<TagRule>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        _repositoryMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(System.Threading.Tasks.Task.CompletedTask);

        // Act
        var result = await _handler.Handle(new ReapplyRulesRequest(), CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.PhotosUpdated.Should().Be(0);
    }
}
