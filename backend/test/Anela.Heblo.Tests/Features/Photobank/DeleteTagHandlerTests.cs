using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank.UseCases.DeleteTag;
using Anela.Heblo.Domain.Features.Photobank;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public class DeleteTagHandlerTests
{
    private readonly Mock<IPhotobankRepository> _repositoryMock;
    private readonly DeleteTagHandler _handler;

    public DeleteTagHandlerTests()
    {
        _repositoryMock = new Mock<IPhotobankRepository>();
        _handler = new DeleteTagHandler(_repositoryMock.Object);
    }

    private static Tag BuildTag(int id, string name, int photoTagCount = 0)
    {
        var tag = new Tag { Id = id, Name = name };
        for (var i = 0; i < photoTagCount; i++)
            tag.PhotoTags.Add(new PhotoTag { PhotoId = i + 1, TagId = id });
        return tag;
    }

    [Fact]
    public async Task Handle_TagExistsWithAssignments_DeletesAndReturnsCorrectCount()
    {
        // Arrange
        var tag = BuildTag(5, "summer", photoTagCount: 3);

        _repositoryMock
            .Setup(r => r.GetTagByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tag);

        _repositoryMock
            .Setup(r => r.DeleteTagAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var request = new DeleteTagRequest { Id = 5 };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Deleted.Should().BeTrue();
        result.RemovedAssignmentCount.Should().Be(3);

        _repositoryMock.Verify(r => r.DeleteTagAsync(5, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_TagExistsWithNoAssignments_DeletesAndReturnsZeroCount()
    {
        // Arrange
        var tag = BuildTag(7, "sale", photoTagCount: 0);

        _repositoryMock
            .Setup(r => r.GetTagByIdAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tag);

        _repositoryMock
            .Setup(r => r.DeleteTagAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var request = new DeleteTagRequest { Id = 7 };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Deleted.Should().BeTrue();
        result.RemovedAssignmentCount.Should().Be(0);

        _repositoryMock.Verify(r => r.DeleteTagAsync(7, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_TagNotFound_ReturnsFalseWithoutCallingDelete()
    {
        // Arrange
        _repositoryMock
            .Setup(r => r.GetTagByIdAsync(99, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tag?)null);

        var request = new DeleteTagRequest { Id = 99 };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Deleted.Should().BeFalse();
        result.RemovedAssignmentCount.Should().Be(0);

        _repositoryMock.Verify(r => r.DeleteTagAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
