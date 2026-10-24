using Anela.Heblo.Application.Features.Catalog;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetManufactureDifficultySettings;
using Anela.Heblo.Domain.Features.Catalog;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class GetManufactureDifficultySettingsHandlerTests
{
    private static readonly DateTime Now = new(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IManufactureDifficultyRepository> _repositoryMock;
    private readonly FakeTimeProvider _timeProvider;
    private readonly GetManufactureDifficultySettingsHandler _handler;

    public GetManufactureDifficultySettingsHandlerTests()
    {
        _repositoryMock = new Mock<IManufactureDifficultyRepository>();
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(Now));

        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<CatalogMappingProfile>(), NullLoggerFactory.Instance)
            .CreateMapper();

        _handler = new GetManufactureDifficultySettingsHandler(
            _repositoryMock.Object,
            mapper,
            new Mock<ILogger<GetManufactureDifficultySettingsHandler>>().Object,
            _timeProvider);
    }

    private static ManufactureDifficultySetting Setting(int id, DateTime? from, DateTime? to) => new()
    {
        Id = id,
        ProductCode = "P1",
        DifficultyValue = 1,
        ValidFrom = from,
        ValidTo = to,
        CreatedAt = Now,
        CreatedBy = "test"
    };

    private void SetupList(params ManufactureDifficultySetting[] settings)
    {
        _repositoryMock
            .Setup(r => r.ListAsync(It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(settings.ToList());
    }

    private static GetManufactureDifficultySettingsRequest Request(DateTime? asOfDate = null) => new()
    {
        ProductCode = "P1",
        AsOfDate = asOfDate
    };

    [Fact]
    public async Task Handle_NullFromAndTo_IsCurrentForAnyDate()
    {
        SetupList(Setting(1, null, null));

        var atNow = await _handler.Handle(Request(), CancellationToken.None);
        var farPast = await _handler.Handle(Request(new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc)), CancellationToken.None);
        var farFuture = await _handler.Handle(Request(new DateTime(2999, 1, 1, 0, 0, 0, DateTimeKind.Utc)), CancellationToken.None);

        foreach (var response in new[] { atNow, farPast, farFuture })
        {
            response.Settings.Should().ContainSingle().Which.IsCurrent.Should().BeTrue();
            response.CurrentSetting.Should().NotBeNull();
            response.CurrentSetting!.Id.Should().Be(1);
        }
    }

    [Theory]
    [InlineData(1, true)]    // ValidTo after reference date
    [InlineData(0, true)]    // ValidTo equal to reference date (inclusive)
    [InlineData(-1, false)]  // ValidTo before reference date
    public async Task Handle_NullValidFrom_CurrentWhenValidToNotBeforeReference(int validToOffsetDays, bool expectedCurrent)
    {
        SetupList(Setting(1, null, Now.AddDays(validToOffsetDays)));

        var response = await _handler.Handle(Request(), CancellationToken.None);

        response.Settings.Should().ContainSingle().Which.IsCurrent.Should().Be(expectedCurrent);
        if (expectedCurrent)
        {
            response.CurrentSetting.Should().NotBeNull();
        }
        else
        {
            response.CurrentSetting.Should().BeNull();
        }
    }

    [Theory]
    [InlineData(-1, true)]   // ValidFrom before reference date
    [InlineData(0, true)]    // ValidFrom equal to reference date (inclusive)
    [InlineData(1, false)]   // ValidFrom after reference date
    public async Task Handle_NullValidTo_CurrentWhenValidFromNotAfterReference(int validFromOffsetDays, bool expectedCurrent)
    {
        SetupList(Setting(1, Now.AddDays(validFromOffsetDays), null));

        var response = await _handler.Handle(Request(), CancellationToken.None);

        response.Settings.Should().ContainSingle().Which.IsCurrent.Should().Be(expectedCurrent);
        if (expectedCurrent)
        {
            response.CurrentSetting.Should().NotBeNull();
        }
        else
        {
            response.CurrentSetting.Should().BeNull();
        }
    }

    [Fact]
    public async Task Handle_OnlyOneSettingContainsReference_OnlyThatOneIsCurrent()
    {
        SetupList(
            Setting(1, Now.AddDays(-30), Now.AddDays(-10)),
            Setting(2, Now.AddDays(-5), Now.AddDays(5)));

        var response = await _handler.Handle(Request(), CancellationToken.None);

        response.Settings.Select(s => s.IsCurrent).Should().Equal(false, true);
        response.CurrentSetting.Should().NotBeNull();
        response.CurrentSetting!.Id.Should().Be(2);
    }

    [Fact]
    public async Task Handle_OverlappingSettings_AllMatchesFlaggedAndFirstIsCurrentSetting()
    {
        SetupList(
            Setting(1, Now.AddDays(-10), Now.AddDays(10)),
            Setting(2, Now.AddDays(-5), Now.AddDays(5)));

        var response = await _handler.Handle(Request(), CancellationToken.None);

        response.Settings.Should().OnlyContain(s => s.IsCurrent);
        response.CurrentSetting.Should().NotBeNull();
        response.CurrentSetting!.Id.Should().Be(1);
    }

    [Fact]
    public async Task Handle_BoundariesEqualToReference_AreInclusive()
    {
        SetupList(
            Setting(1, Now, Now.AddDays(10)),
            Setting(2, Now.AddDays(-10), Now));

        var response = await _handler.Handle(Request(), CancellationToken.None);

        response.Settings.Should().OnlyContain(s => s.IsCurrent);
    }

    [Fact]
    public async Task Handle_AsOfDateSupplied_UsesAsOfDateInsteadOfNow()
    {
        var asOf = Now.AddDays(-100);
        // valid at asOf only, not at now
        SetupList(Setting(1, asOf.AddDays(-5), asOf.AddDays(5)));

        var response = await _handler.Handle(Request(asOf), CancellationToken.None);

        response.Settings.Should().ContainSingle().Which.IsCurrent.Should().BeTrue();
        response.CurrentSetting.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_AsOfDateSupplied_SettingValidOnlyNowIsNotCurrent()
    {
        var asOf = Now.AddDays(-100);
        // valid at now only, not at asOf
        SetupList(Setting(1, Now.AddDays(-5), Now.AddDays(5)));

        var response = await _handler.Handle(Request(asOf), CancellationToken.None);

        response.Settings.Should().ContainSingle().Which.IsCurrent.Should().BeFalse();
        response.CurrentSetting.Should().BeNull();
    }

    [Fact]
    public async Task Handle_AsOfDateSupplied_PassesAsOfDateToRepository()
    {
        var asOf = Now.AddDays(-100);
        SetupList();

        await _handler.Handle(Request(asOf), CancellationToken.None);

        _repositoryMock.Verify(r => r.ListAsync("P1", asOf, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NoAsOfDate_UsesTimeProviderNow()
    {
        SetupList(Setting(1, Now.AddDays(-5), Now.AddDays(5)));

        var atNow = await _handler.Handle(Request(), CancellationToken.None);
        atNow.Settings.Single().IsCurrent.Should().BeTrue();

        _timeProvider.SetUtcNow(new DateTimeOffset(Now.AddDays(100)));
        var later = await _handler.Handle(Request(), CancellationToken.None);
        later.Settings.Single().IsCurrent.Should().BeFalse();

        _repositoryMock.Verify(r => r.ListAsync("P1", null, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Handle_NoSettingMatches_CurrentSettingNullAndNoneFlagged()
    {
        SetupList(
            Setting(1, Now.AddDays(-30), Now.AddDays(-10)),
            Setting(2, Now.AddDays(10), Now.AddDays(30)));

        var response = await _handler.Handle(Request(), CancellationToken.None);

        response.CurrentSetting.Should().BeNull();
        response.Settings.Should().HaveCount(2);
        response.Settings.Should().OnlyContain(s => !s.IsCurrent);
        response.ProductCode.Should().Be("P1");
    }

    [Fact]
    public async Task Handle_EmptyRepository_ReturnsEmptyResponseWithProductCode()
    {
        SetupList();

        var response = await _handler.Handle(Request(), CancellationToken.None);

        response.Settings.Should().BeEmpty();
        response.CurrentSetting.Should().BeNull();
        response.ProductCode.Should().Be("P1");
    }
}
