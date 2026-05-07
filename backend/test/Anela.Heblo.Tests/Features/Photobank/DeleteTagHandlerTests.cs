using System.Threading;
using System.Threading.Tasks;
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

    private DeleteTagHandler CreateHandler() => new(_repositoryMock.Object);

    private static Tag BuildTag(int id, string name) => new() { Id = id, Name = name };

    [Fact]
    public async Task Handle_TagNotFound_ReturnsPhotobankTagNotFoundError()
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

    [Fact]
    public async Task Handle_TagExists_PassesEntityToDeleteTagAsync()
    {
        // Arrange
        var tag = BuildTag(5, "promo");

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
        result.ErrorCode.Should().BeNull();

        _repositoryMock.Verify(
            r => r.DeleteTagAsync(tag, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_TagExists_DoesNotCallDeleteWithWrongEntity()
    {
        // Arrange
        var tag = BuildTag(3, "sale");
        var otherTag = BuildTag(7, "other");

        _repositoryMock
            .Setup(r => r.GetTagByIdAsync(3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tag);

        _repositoryMock
            .Setup(r => r.DeleteTagAsync(tag, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new DeleteTagRequest { Id = 3 };

        // Act
        await CreateHandler().Handle(request, CancellationToken.None);

        // Assert
        _repositoryMock.Verify(
            r => r.DeleteTagAsync(otherTag, It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
