using Anela.Heblo.Application.Features.GiftSettings.Dto;
using Anela.Heblo.Application.Features.GiftSettings.UseCases.GetGiftSetting;
using Anela.Heblo.Domain.Features.Logistics.GiftSettings;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Application.GiftSettings;

public class GetGiftSettingHandlerTests
{
    private readonly Mock<IGiftSettingRepository> _repositoryMock = new();
    private readonly GetGiftSettingHandler _sut;

    public GetGiftSettingHandlerTests()
    {
        _sut = new GetGiftSettingHandler(_repositoryMock.Object);
    }

    [Fact]
    public async Task Handle_ReturnsDefault_WhenNoRowExists()
    {
        _repositoryMock.Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(GiftSetting.CreateDefault());

        var result = await _sut.Handle(new GetGiftSettingQuery(), CancellationToken.None);

        result.IsEnabled.Should().BeFalse();
        result.ThresholdCzk.Should().Be(0m);
        result.Text.Should().BeEmpty();
        result.ModifiedAt.Should().BeNull();
        result.ModifiedBy.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ReturnsSavedValues_WhenRowExists()
    {
        var setting = new GiftSetting(true, 1500m, "DÁREK ZDARMA", "user-1");
        _repositoryMock.Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(setting);

        var result = await _sut.Handle(new GetGiftSettingQuery(), CancellationToken.None);

        result.IsEnabled.Should().BeTrue();
        result.ThresholdCzk.Should().Be(1500m);
        result.Text.Should().Be("DÁREK ZDARMA");
        result.ModifiedAt.Should().NotBeNull();
        result.ModifiedBy.Should().Be("user-1");
    }
}
