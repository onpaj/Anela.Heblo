using Anela.Heblo.Application.Features.Packaging.UseCases.GetCarrierLabelOffsets;
using Anela.Heblo.Application.Features.Packaging.UseCases.SetCarrierLabelOffset;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Domain.Features.Packaging;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Application.Packaging;

public class CarrierLabelOffsetHandlerTests
{
    private readonly Mock<ICarrierLabelOffsetRepository> _repository = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();

    public CarrierLabelOffsetHandlerTests()
    {
        _currentUser.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser(Id: "user-123", Name: "Test", Email: null, IsAuthenticated: true));
    }

    [Fact]
    public async Task Get_ReturnsEveryLabelCarrier_UnsavedOnesAsZero()
    {
        _repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new CarrierLabelOffset(Carriers.Zasilkovna, 3, -5, "user-1") });

        var response = await new GetCarrierLabelOffsetsHandler(_repository.Object)
            .Handle(new GetCarrierLabelOffsetsRequest(), CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Offsets.Select(o => (o.Carrier, o.OffsetXMm, o.OffsetYMm)).Should().Equal(
            (Carriers.Zasilkovna, 3, -5),
            (Carriers.PPL, 0, 0),
            (Carriers.GLS, 0, 0));
        response.Offsets[0].ModifiedBy.Should().Be("user-1");
        response.Offsets[1].ModifiedAt.Should().BeNull();
    }

    [Fact]
    public async Task Set_UpsertsOffsetAsCurrentUser()
    {
        var request = new SetCarrierLabelOffsetRequest { Carrier = Carriers.PPL, OffsetXMm = -4, OffsetYMm = 7 };

        var response = await new SetCarrierLabelOffsetHandler(_repository.Object, _currentUser.Object)
            .Handle(request, CancellationToken.None);

        response.Success.Should().BeTrue();
        _repository.Verify(r => r.UpsertAsync(
            It.Is<CarrierLabelOffset>(o =>
                o.Carrier == Carriers.PPL && o.OffsetXMm == -4 && o.OffsetYMm == 7 && o.ModifiedBy == "user-123"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Set_NoCurrentUser_ReturnsUnauthorizedAndSavesNothing()
    {
        _currentUser.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser(Id: null, Name: null, Email: null, IsAuthenticated: false));

        var response = await new SetCarrierLabelOffsetHandler(_repository.Object, _currentUser.Object)
            .Handle(new SetCarrierLabelOffsetRequest { Carrier = Carriers.PPL }, CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.Unauthorized);
        _repository.Verify(r => r.UpsertAsync(It.IsAny<CarrierLabelOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(Carriers.Zasilkovna, 0, 0, true)]
    [InlineData(Carriers.PPL, -30, 30, true)]
    [InlineData(Carriers.GLS, 30, -30, true)]
    [InlineData(Carriers.Osobak, 0, 0, false)]
    [InlineData((Carriers)99, 0, 0, false)]
    [InlineData(Carriers.PPL, 31, 0, false)]
    [InlineData(Carriers.PPL, 0, -31, false)]
    public void Validator_AcceptsLabelCarriersWithinRange(Carriers carrier, int xMm, int yMm, bool isValid)
    {
        var result = new SetCarrierLabelOffsetValidator().Validate(
            new SetCarrierLabelOffsetRequest { Carrier = carrier, OffsetXMm = xMm, OffsetYMm = yMm });

        result.IsValid.Should().Be(isValid);
    }
}
