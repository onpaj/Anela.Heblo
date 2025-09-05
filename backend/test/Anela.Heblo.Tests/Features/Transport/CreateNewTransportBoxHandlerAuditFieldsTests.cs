using Anela.Heblo.Application.Features.Transport.UseCases.CreateNewTransportBox;
using Anela.Heblo.Application.Features.Transport.UseCases;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Users;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Transport;

public class CreateNewTransportBoxHandlerAuditFieldsTests
{
    private readonly Mock<ITransportBoxRepository> _repositoryMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<ILogger<CreateNewTransportBoxHandler>> _loggerMock;
    private readonly Mock<IMapper> _mapperMock;
    private readonly CreateNewTransportBoxHandler _handler;

    public CreateNewTransportBoxHandlerAuditFieldsTests()
    {
        _repositoryMock = new Mock<ITransportBoxRepository>();
        _timeProviderMock = new Mock<TimeProvider>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _loggerMock = new Mock<ILogger<CreateNewTransportBoxHandler>>();
        _mapperMock = new Mock<IMapper>();

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("12345678-1234-1234-1234-123456789012", "Test User", "test@example.com", true));

        _timeProviderMock
            .Setup(x => x.GetUtcNow())
            .Returns(new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero));

        _handler = new CreateNewTransportBoxHandler(
            _repositoryMock.Object,
            _timeProviderMock.Object,
            _currentUserServiceMock.Object,
            _loggerMock.Object,
            _mapperMock.Object);
    }

    [Fact]
    public async Task Handle_CreateNewTransportBox_ShouldPopulateAuditFields()
    {
        // Arrange
        var request = new CreateNewTransportBoxRequest { Description = "Test Box" };
        TransportBox? capturedTransportBox = null;

        _repositoryMock
            .Setup(x => x.AddAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()))
            .Callback<TransportBox, CancellationToken>((box, _) => capturedTransportBox = box)
            .ReturnsAsync((TransportBox box, CancellationToken _) => box);

        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        capturedTransportBox.Should().NotBeNull();
        capturedTransportBox!.CreationTime.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
        capturedTransportBox.ConcurrencyStamp.Should().NotBeNullOrEmpty();
        capturedTransportBox.CreatorId.Should().Be(Guid.Parse("12345678-1234-1234-1234-123456789012"));
        capturedTransportBox.Description.Should().Be("Test Box");
        capturedTransportBox.ExtraProperties.Should().Be("{}");
    }

    [Fact]
    public async Task Handle_CreateNewTransportBox_ShouldHaveValidConcurrencyStamp()
    {
        // Arrange
        var request = new CreateNewTransportBoxRequest { Description = "Test Box" };
        TransportBox? capturedTransportBox = null;

        _repositoryMock
            .Setup(x => x.AddAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()))
            .Callback<TransportBox, CancellationToken>((box, _) => capturedTransportBox = box)
            .ReturnsAsync((TransportBox box, CancellationToken _) => box);

        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        capturedTransportBox.Should().NotBeNull();
        capturedTransportBox!.ConcurrencyStamp.Should().NotBeNullOrEmpty();

        // Should be a valid GUID format
        Guid.TryParse(capturedTransportBox.ConcurrencyStamp, out _).Should().BeTrue();
    }

    [Fact]
    public async Task Handle_CreateNewTransportBox_ShouldHaveValidExtraProperties()
    {
        // Arrange
        var request = new CreateNewTransportBoxRequest { Description = "Test Box" };
        TransportBox? capturedTransportBox = null;

        _repositoryMock
            .Setup(x => x.AddAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()))
            .Callback<TransportBox, CancellationToken>((box, _) => capturedTransportBox = box)
            .ReturnsAsync((TransportBox box, CancellationToken _) => box);

        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        capturedTransportBox.Should().NotBeNull();
        capturedTransportBox!.ExtraProperties.Should().NotBeNullOrEmpty();
        capturedTransportBox.ExtraProperties.Should().Be("{}");

        // Should be valid JSON
        System.Text.Json.JsonDocument.Parse(capturedTransportBox.ExtraProperties).Should().NotBeNull();
    }
}