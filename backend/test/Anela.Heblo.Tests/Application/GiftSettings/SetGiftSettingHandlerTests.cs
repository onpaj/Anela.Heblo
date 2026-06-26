using Anela.Heblo.Application.Features.GiftSettings.UseCases.SetGiftSetting;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics.GiftSettings;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Application.GiftSettings;

public class SetGiftSettingHandlerTests
{
    private readonly Mock<IGiftSettingRepository> _repositoryMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();
    private readonly SetGiftSettingHandler _sut;

    public SetGiftSettingHandlerTests()
    {
        _currentUserMock.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser(Id: "user-1", Name: "Test", Email: null, IsAuthenticated: true));
        _sut = new SetGiftSettingHandler(_repositoryMock.Object, _currentUserMock.Object);
    }

    [Fact]
    public async Task Handle_SavesSetting_WhenDisabled()
    {
        var command = new SetGiftSettingCommand
        {
            IsEnabled = false,
            ThresholdCzk = 0,
            Text = string.Empty,
        };

        var result = await _sut.Handle(command, CancellationToken.None);

        result.Success.Should().BeTrue();
        _repositoryMock.Verify(r => r.SaveAsync(It.Is<GiftSetting>(g => g.ModifiedBy == "user-1"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SavesSetting_WhenEnabledWithValidValues()
    {
        var command = new SetGiftSettingCommand
        {
            IsEnabled = true,
            ThresholdCzk = 1500m,
            Text = "DÁREK ZDARMA",
        };

        var result = await _sut.Handle(command, CancellationToken.None);

        result.Success.Should().BeTrue();
        _repositoryMock.Verify(r => r.SaveAsync(It.Is<GiftSetting>(g => g.ModifiedBy == "user-1"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenEnabledWithZeroThreshold()
    {
        var command = new SetGiftSettingCommand
        {
            IsEnabled = true,
            ThresholdCzk = 0,
            Text = "DÁREK ZDARMA",
        };

        var result = await _sut.Handle(command, CancellationToken.None);

        result.Success.Should().BeFalse();
        _repositoryMock.Verify(r => r.SaveAsync(It.IsAny<GiftSetting>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenEnabledWithEmptyText()
    {
        var command = new SetGiftSettingCommand
        {
            IsEnabled = true,
            ThresholdCzk = 1500m,
            Text = string.Empty,
        };

        var result = await _sut.Handle(command, CancellationToken.None);

        result.Success.Should().BeFalse();
        _repositoryMock.Verify(r => r.SaveAsync(It.IsAny<GiftSetting>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenTextExceedsMaxLength()
    {
        var command = new SetGiftSettingCommand
        {
            IsEnabled = false,
            ThresholdCzk = 0,
            Text = new string('X', 51),
        };

        var result = await _sut.Handle(command, CancellationToken.None);

        result.Success.Should().BeFalse();
        _repositoryMock.Verify(r => r.SaveAsync(It.IsAny<GiftSetting>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsUnauthorized_WhenCurrentUserIdIsNullOrEmpty()
    {
        _currentUserMock.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser(Id: null, Name: null, Email: null, IsAuthenticated: false));

        var command = new SetGiftSettingCommand
        {
            IsEnabled = false,
            ThresholdCzk = 0,
            Text = string.Empty,
        };

        var result = await _sut.Handle(command, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Unauthorized);
        _repositoryMock.Verify(r => r.SaveAsync(It.IsAny<GiftSetting>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
