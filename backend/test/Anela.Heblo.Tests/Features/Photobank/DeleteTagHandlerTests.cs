using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Application.Features.Photobank.UseCases.DeleteTag;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Photobank;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public class DeleteTagHandlerTests
{
    private readonly Mock<IPhotobankRepository> _repositoryMock = new();
    private readonly Mock<IPhotobankTagsCache> _cacheMock = new();

    private DeleteTagHandler CreateHandler() => new(_repositoryMock.Object, _cacheMock.Object);

    private static Tag BuildTag(int id, string name, int photoTagCount = 0)
    {
        var tag = new Tag { Id = id, Name = name };
        for (var i = 0; i < photoTagCount; i++)
            tag.PhotoTags.Add(new PhotoTag { PhotoId = i + 1, TagId = id });
        return tag;
    }

    [Fact]
    public async Task Handle_TagExistsWithAssignments_ReturnsSuccessWithCorrectCount()
    {
        // Arrange
        var tag = BuildTag(5, "summer", photoTagCount: 3);

        _repositoryMock
            .Setup(r => r.GetTagByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tag);

        _repositoryMock
            .Setup(r => r.DeleteTagAsync(tag, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new DeleteTagRequest { Id = 5 };

        // Act
        var result = await CreateHandler().Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.RemovedAssignmentCount.Should().Be(3);

        _repositoryMock.Verify(r => r.DeleteTagAsync(tag, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_TagExistsWithNoAssignments_ReturnsSuccessWithZeroCount()
    {
        // Arrange
        var tag = BuildTag(7, "sale", photoTagCount: 0);

        _repositoryMock
            .Setup(r => r.GetTagByIdAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tag);

        _repositoryMock
            .Setup(r => r.DeleteTagAsync(tag, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new DeleteTagRequest { Id = 7 };

        // Act
        var result = await CreateHandler().Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.RemovedAssignmentCount.Should().Be(0);

        _repositoryMock.Verify(r => r.DeleteTagAsync(tag, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_TagNotFound_ReturnsNotFoundErrorWithoutCallingDelete()
    {
        // Arrange
        _repositoryMock
            .Setup(r => r.GetTagByIdAsync(99, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tag?)null);

        var request = new DeleteTagRequest { Id = 99 };

        // Act
        var result = await CreateHandler().Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.PhotobankTagNotFound);

        _repositoryMock.Verify(r => r.DeleteTagAsync(It.IsAny<Tag>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
