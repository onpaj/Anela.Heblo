using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.SetLotLabelCalibration;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class SetLotLabelCalibrationHandlerTests
{
    private static Mock<ICurrentUserService> AuthenticatedUser() =>
        new Mock<ICurrentUserService>().SetupUser("admin");

    [Fact]
    public async Task Handle_SavesPitch_WhenAuthenticated()
    {
        var repo = new Mock<ILotLabelCalibrationRepository>();
        var sut = new SetLotLabelCalibrationHandler(
            repo.Object, AuthenticatedUser().Object, NullLogger<SetLotLabelCalibrationHandler>.Instance);

        var result = await sut.Handle(
            new SetLotLabelCalibrationRequest { PitchDots = 152, DriftDotsPer100Labels = 4 }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.PitchDots.Should().Be(152);
        result.DriftDotsPer100Labels.Should().Be(4);
        repo.Verify(r => r.SaveAsync(
            It.Is<LotLabelCalibration>(c => c.PitchDots == 152 && c.DriftDotsPer100Labels == 4),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsUnauthorized_WhenNoUser()
    {
        var repo = new Mock<ILotLabelCalibrationRepository>();
        var user = new Mock<ICurrentUserService>();
        user.Setup(u => u.GetCurrentUser())
            .Returns(new CurrentUser(Id: "", Name: null, Email: null, IsAuthenticated: false));

        var sut = new SetLotLabelCalibrationHandler(
            repo.Object, user.Object, NullLogger<SetLotLabelCalibrationHandler>.Instance);

        var result = await sut.Handle(new SetLotLabelCalibrationRequest { PitchDots = 152 }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Unauthorized);
        repo.Verify(r => r.SaveAsync(It.IsAny<LotLabelCalibration>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

internal static class CurrentUserServiceMockExtensions
{
    public static Mock<ICurrentUserService> SetupUser(this Mock<ICurrentUserService> mock, string id)
    {
        mock.Setup(u => u.GetCurrentUser())
            .Returns(new CurrentUser(Id: id, Name: id, Email: $"{id}@example.com", IsAuthenticated: true));
        return mock;
    }
}
