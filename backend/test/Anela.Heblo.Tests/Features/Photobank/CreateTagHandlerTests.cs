using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Application.Features.Photobank.UseCases.CreateTag;
using Anela.Heblo.Domain.Features.Photobank;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public class CreateTagHandlerTests
{
    private readonly Mock<IPhotobankRepository> _repositoryMock;
    private readonly Mock<IPhotobankTagsCache> _cacheMock = new();
    private readonly CreateTagHandler _handler;

    public CreateTagHandlerTests()
    {
        _repositoryMock = new Mock<IPhotobankRepository>();
        _handler = new CreateTagHandler(_repositoryMock.Object, _cacheMock.Object);
    }

    private static Tag BuildTag(int id, string name) => new() { Id = id, Name = name };

    [Fact]
    public async Task Handle_NewTag_CallsGetOrCreateAndReturnsAlreadyExistedFalse()
    {
        // Arrange
        var createdTag = BuildTag(42, "summer");

        _repositoryMock
            .Setup(r => r.GetTagByNameAsync("summer", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tag?)null);

        _repositoryMock
            .Setup(r => r.GetOrCreateTagAsync("summer", It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdTag);

        var request = new CreateTagRequest { Name = "summer" };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Id.Should().Be(42);
        result.Name.Should().Be("summer");
        result.AlreadyExisted.Should().BeFalse();

        _repositoryMock.Verify(r => r.GetOrCreateTagAsync("summer", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ExistingTag_ReturnsAlreadyExistedTrueWithoutCallingGetOrCreate()
    {
        // Arrange
        var existingTag = BuildTag(7, "sale");

        _repositoryMock
            .Setup(r => r.GetTagByNameAsync("sale", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingTag);

        var request = new CreateTagRequest { Name = "sale" };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Id.Should().Be(7);
        result.Name.Should().Be("sale");
        result.AlreadyExisted.Should().BeTrue();

        _repositoryMock.Verify(r => r.GetOrCreateTagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("HELLO World", "hello world")]
    [InlineData("  Summer  ", "summer")]
    public async Task Handle_NormalizableInput_PassesNormalizedNameToRepositoryCalls(string input, string expectedNormalized)
    {
        // Arrange
        var createdTag = BuildTag(10, expectedNormalized);

        _repositoryMock
            .Setup(r => r.GetTagByNameAsync(expectedNormalized, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tag?)null);

        _repositoryMock
            .Setup(r => r.GetOrCreateTagAsync(expectedNormalized, It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdTag);

        var request = new CreateTagRequest { Name = input };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Name.Should().Be(expectedNormalized);
        result.AlreadyExisted.Should().BeFalse();

        _repositoryMock.Verify(r => r.GetTagByNameAsync(expectedNormalized, It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.GetOrCreateTagAsync(expectedNormalized, It.IsAny<CancellationToken>()), Times.Once);
    }
}
