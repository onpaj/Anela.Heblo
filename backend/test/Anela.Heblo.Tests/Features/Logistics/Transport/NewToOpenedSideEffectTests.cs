using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.Transport;

public class NewToOpenedSideEffectTests
{
    private readonly Mock<ITransportBoxRepository> _repositoryMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly NewToOpenedSideEffect _sideEffect;

    public NewToOpenedSideEffectTests()
    {
        _repositoryMock = new Mock<ITransportBoxRepository>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _timeProviderMock = new Mock<TimeProvider>();

        // Setup default returns
        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("tester", "Tester", "tester@test.com", true));

        _timeProviderMock
            .Setup(x => x.GetUtcNow())
            .Returns(new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero));

        _sideEffect = new NewToOpenedSideEffect(
            _repositoryMock.Object,
            _currentUserServiceMock.Object,
            _timeProviderMock.Object);
    }

    [Fact]
    public void Supports_NewToOpened_ReturnsTrue()
    {
        // Arrange & Act
        var result = _sideEffect.Supports(TransportBoxState.New, TransportBoxState.Opened);

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData(TransportBoxState.Opened, TransportBoxState.Reserve)]
    [InlineData(TransportBoxState.New, TransportBoxState.Quarantine)]
    public void Supports_AnyOtherPair_ReturnsFalse(TransportBoxState from, TransportBoxState to)
    {
        // Arrange & Act
        var result = _sideEffect.Supports(from, to);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_MissingBoxCode_ReturnsRequiredFieldMissing()
    {
        // Arrange
        var box = new TransportBox();
        var request = new ChangeTransportBoxStateRequest
        {
            BoxId = 1,
            NewState = TransportBoxState.Opened
        };

        // Act
        var result = await _sideEffect.ExecuteAsync(box, request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.RequiredFieldMissing);
        result.Params.Should().ContainKey("field");
        result.Params!["field"].Should().Be("BoxCode");
    }

    [Fact]
    public async Task ExecuteAsync_DuplicateActiveCode_ReturnsDuplicateActiveBoxFound()
    {
        // Arrange
        var box = new TransportBox();
        var request = new ChangeTransportBoxStateRequest
        {
            BoxId = 1,
            NewState = TransportBoxState.Opened,
            BoxCode = "b999"
        };

        _repositoryMock
            .Setup(x => x.IsBoxCodeActiveAsync("B999"))
            .ReturnsAsync(true);

        // Act
        var result = await _sideEffect.ExecuteAsync(box, request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.TransportBoxDuplicateActiveBoxFound);
        result.Params.Should().ContainKey("code");
        result.Params!["code"].Should().Be("B999");
    }

    [Fact]
    public async Task ExecuteAsync_ValidCode_ClosesStaleStockedBoxesWithSameCode_ReturnsNull()
    {
        // Arrange
        var box = new TransportBox();
        var request = new ChangeTransportBoxStateRequest
        {
            BoxId = 1,
            NewState = TransportBoxState.Opened,
            BoxCode = "B999"
        };

        var staleBox = new TransportBox();

        _repositoryMock
            .Setup(x => x.IsBoxCodeActiveAsync("B999"))
            .ReturnsAsync(false);

        _repositoryMock
            .Setup(x => x.GetPagedListAsync(
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<TransportBoxState?>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<bool>()))
            .ReturnsAsync((new List<TransportBox> { staleBox }, 1));

        _repositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sideEffect.ExecuteAsync(box, request, CancellationToken.None);

        // Assert
        result.Should().BeNull();
        _repositoryMock.Verify(
            x => x.UpdateAsync(staleBox, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
