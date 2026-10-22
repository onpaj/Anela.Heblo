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
}
