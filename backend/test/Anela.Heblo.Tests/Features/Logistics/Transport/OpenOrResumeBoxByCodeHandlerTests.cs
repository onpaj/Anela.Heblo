using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Features.Logistics.UseCases.OpenOrResumeBoxByCode;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Users;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.Transport;

public class OpenOrResumeBoxByCodeHandlerTests
{
    private static readonly DateTime FixedTime = new(2026, 5, 16, 8, 0, 0, DateTimeKind.Utc);

    private readonly Mock<ITransportBoxRepository> _repositoryMock = new();
    private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();
    private readonly Mock<ILogger<OpenOrResumeBoxByCodeHandler>> _loggerMock = new();
    private readonly Mock<IMapper> _mapperMock = new();
    private readonly Mock<TimeProvider> _timeProviderMock = new();
    private readonly OpenOrResumeBoxByCodeHandler _handler;

    public OpenOrResumeBoxByCodeHandlerTests()
    {
        _currentUserServiceMock.Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("00000000-0000-0000-0000-000000000001", "Test User", "test@example.com", true));
        _timeProviderMock.Setup(x => x.GetUtcNow()).Returns(new DateTimeOffset(FixedTime));
        _mapperMock.Setup(x => x.Map<TransportBoxDto>(It.IsAny<TransportBox>())).Returns(new TransportBoxDto());
        _repositoryMock.Setup(r => r.AddAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TransportBox b, CancellationToken _) => b);
        _repositoryMock.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _handler = new OpenOrResumeBoxByCodeHandler(
            _repositoryMock.Object,
            _currentUserServiceMock.Object,
            _loggerMock.Object,
            _mapperMock.Object,
            _timeProviderMock.Object);
    }

    private static TransportBox OpenedBox(string code)
    {
        var box = new TransportBox { ConcurrencyStamp = Guid.NewGuid().ToString(), ExtraProperties = "{}" };
        box.Open(code, FixedTime, "Test User");
        return box;
    }

    private static TransportBox ClosedBox(string code)
    {
        var box = OpenedBox(code);
        box.AddItem("P-1", "Product 1", 1, FixedTime, "Test User");
        box.ToTransit(FixedTime, "Test User");
        box.Receive(FixedTime, "Test User");
        box.ToPick(FixedTime, "Test User");
        box.Close(FixedTime, "Test User");
        return box;
    }

    private static TransportBox InTransitBox(string code)
    {
        var box = OpenedBox(code);
        box.AddItem("P-1", "Product 1", 1, FixedTime, "Test User");
        box.ToTransit(FixedTime, "Test User");
        return box;
    }

    private static TransportBox StockedBox(string code)
    {
        var box = OpenedBox(code);
        box.AddItem("P-1", "Product 1", 1, FixedTime, "Test User");
        box.ToTransit(FixedTime, "Test User");
        box.Receive(FixedTime, "Test User");
        box.ToPick(FixedTime, "Test User");
        return box;
    }

    [Fact]
    public async Task Handle_EmptyCode_ReturnsRequiredFieldMissing()
    {
        var result = await _handler.Handle(new OpenOrResumeBoxByCodeRequest { BoxCode = "  " }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.RequiredFieldMissing);
    }

    [Fact]
    public async Task Handle_InvalidCodeFormat_ReturnsValidationError()
    {
        _repositoryMock.Setup(r => r.GetByCodeAsync(It.IsAny<string>())).ReturnsAsync((TransportBox?)null);

        var result = await _handler.Handle(new OpenOrResumeBoxByCodeRequest { BoxCode = "XYZ" }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task Handle_NoExistingBox_CreatesAndOpensNewBox()
    {
        _repositoryMock.Setup(r => r.GetByCodeAsync("B001")).ReturnsAsync((TransportBox?)null);
        TransportBox? added = null;
        _repositoryMock.Setup(r => r.AddAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TransportBox b, CancellationToken _) => b)
            .Callback<TransportBox, CancellationToken>((b, _) => added = b);

        var result = await _handler.Handle(new OpenOrResumeBoxByCodeRequest { BoxCode = "B001" }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Resumed.Should().BeFalse();
        added.Should().NotBeNull();
        added!.State.Should().Be(TransportBoxState.Opened);
        added.Code.Should().Be("B001");
    }

    [Fact]
    public async Task Handle_ExistingOpenedBox_ResumesWithoutCreating()
    {
        _repositoryMock.Setup(r => r.GetByCodeAsync("B001")).ReturnsAsync(OpenedBox("B001"));

        var result = await _handler.Handle(new OpenOrResumeBoxByCodeRequest { BoxCode = "B001" }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Resumed.Should().BeTrue();
        _repositoryMock.Verify(r => r.AddAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ExistingClosedBox_CreatesNewBox()
    {
        _repositoryMock.Setup(r => r.GetByCodeAsync("B001")).ReturnsAsync(ClosedBox("B001"));

        var result = await _handler.Handle(new OpenOrResumeBoxByCodeRequest { BoxCode = "B001" }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Resumed.Should().BeFalse();
        _repositoryMock.Verify(r => r.AddAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ExistingStockedBox_CreatesNewBox()
    {
        _repositoryMock.Setup(r => r.GetByCodeAsync("B001")).ReturnsAsync(StockedBox("B001"));

        var result = await _handler.Handle(new OpenOrResumeBoxByCodeRequest { BoxCode = "B001" }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Resumed.Should().BeFalse();
        _repositoryMock.Verify(r => r.AddAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_BoxBusyInTransit_ReturnsDuplicateActiveBoxFound()
    {
        _repositoryMock.Setup(r => r.GetByCodeAsync("B001")).ReturnsAsync(InTransitBox("B001"));

        var result = await _handler.Handle(new OpenOrResumeBoxByCodeRequest { BoxCode = "B001" }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.TransportBoxDuplicateActiveBoxFound);
        _repositoryMock.Verify(r => r.AddAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
