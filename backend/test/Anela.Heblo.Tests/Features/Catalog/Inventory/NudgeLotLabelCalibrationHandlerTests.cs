using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.NudgeLotLabelCalibration;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using FluentValidation.TestHelper;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class NudgeLotLabelCalibrationHandlerTests
{
    private static Mock<ILotLabelCalibrationRepository> RepositoryWith(int pitchDots, int driftDotsPer100Labels)
    {
        var repo = new Mock<ILotLabelCalibrationRepository>();
        repo.Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LotLabelCalibration(pitchDots, driftDotsPer100Labels, "previous-user"));
        return repo;
    }

    private static NudgeLotLabelCalibrationHandler Sut(
        ILotLabelCalibrationRepository repository, ICurrentUserService currentUserService) =>
        new(repository, currentUserService, NullLogger<NudgeLotLabelCalibrationHandler>.Instance);

    private static Mock<ICurrentUserService> AuthenticatedUser()
    {
        var mock = new Mock<ICurrentUserService>();
        mock.Setup(u => u.GetCurrentUser())
            .Returns(new CurrentUser(Id: "operator", Name: "operator", Email: "operator@example.com", IsAuthenticated: true));
        return mock;
    }

    [Fact]
    public async Task Handle_AppliesTheNudgeToTheStoredCalibration_AndPersistsIt()
    {
        var repo = RepositoryWith(148, 30);
        var sut = Sut(repo.Object, AuthenticatedUser().Object);

        var result = await sut.Handle(
            new NudgeLotLabelCalibrationRequest
            {
                Direction = LabelDriftDirection.Down,
                Speed = LabelDriftSpeed.Fast,
            },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.IsAtLimit.Should().BeFalse();
        repo.Verify(r => r.SaveAsync(
            It.Is<LotLabelCalibration>(c => c.PitchDots == 148 && c.DriftDotsPer100Labels == 60),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_RecordsTheNudgingUser()
    {
        var repo = RepositoryWith(148, 30);
        var sut = Sut(repo.Object, AuthenticatedUser().Object);

        await sut.Handle(
            new NudgeLotLabelCalibrationRequest
            {
                Direction = LabelDriftDirection.Down,
                Speed = LabelDriftSpeed.Slow,
            },
            CancellationToken.None);

        repo.Verify(r => r.SaveAsync(
            It.Is<LotLabelCalibration>(c => c.ModifiedBy == "operator"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReportsTheLimit_AndSavesNothing_WhenTheCalibrationCannotMoveFurther()
    {
        // Saving here would rewrite ModifiedBy/ModifiedAt for a change that did not happen,
        // and the wizard would confirm an adjustment the operator will never see.
        var repo = RepositoryWith(LotLabelCalibration.MaxPitchDots, 99);
        var sut = Sut(repo.Object, AuthenticatedUser().Object);

        var result = await sut.Handle(
            new NudgeLotLabelCalibrationRequest
            {
                Direction = LabelDriftDirection.Down,
                Speed = LabelDriftSpeed.Fast,
            },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.IsAtLimit.Should().BeTrue();
        repo.Verify(r => r.SaveAsync(It.IsAny<LotLabelCalibration>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("PitchDots")]
    [InlineData("DriftDotsPer100Labels")]
    public void Response_DoesNotCarryTheRawCalibrationNumbers(string propertyName)
    {
        // Those values are served by GetLabelCalibration behind their own read permission,
        // which this endpoint deliberately does not require. Echoing them back here would
        // hand every operator a way to read them anyway.
        typeof(NudgeLotLabelCalibrationResponse).GetProperties()
            .Select(p => p.Name)
            .Should().NotContain(propertyName);
    }

    [Fact]
    public async Task Handle_ReturnsUnauthorized_WhenNoUser()
    {
        var repo = RepositoryWith(148, 30);
        var user = new Mock<ICurrentUserService>();
        user.Setup(u => u.GetCurrentUser())
            .Returns(new CurrentUser(Id: "", Name: null, Email: null, IsAuthenticated: false));

        var result = await Sut(repo.Object, user.Object).Handle(
            new NudgeLotLabelCalibrationRequest
            {
                Direction = LabelDriftDirection.Up,
                Speed = LabelDriftSpeed.Fast,
            },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Unauthorized);
        repo.Verify(r => r.SaveAsync(It.IsAny<LotLabelCalibration>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

public class NudgeLotLabelCalibrationRequestValidatorTests
{
    private readonly NudgeLotLabelCalibrationRequestValidator _validator = new();

    [Fact]
    public void Accepts_ADefinedDirectionAndSpeed()
    {
        var result = _validator.TestValidate(new NudgeLotLabelCalibrationRequest
        {
            Direction = LabelDriftDirection.Down,
            Speed = LabelDriftSpeed.Slow,
        });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Rejects_AnUnsetDirection()
    {
        // The enums start at 1, so an omitted field deserializes to 0 and must be refused
        // rather than silently corrected in whichever direction happens to be first.
        var result = _validator.TestValidate(new NudgeLotLabelCalibrationRequest
        {
            Direction = default,
            Speed = LabelDriftSpeed.Fast,
        });

        result.ShouldHaveValidationErrorFor(x => x.Direction);
    }

    [Fact]
    public void Rejects_AnUnsetSpeed()
    {
        var result = _validator.TestValidate(new NudgeLotLabelCalibrationRequest
        {
            Direction = LabelDriftDirection.Up,
            Speed = default,
        });

        result.ShouldHaveValidationErrorFor(x => x.Speed);
    }
}
