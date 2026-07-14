using Anela.Heblo.Application.Features.Logistics;
using Anela.Heblo.Application.Features.Logistics.UseCases.GetTransportBoxes;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.Transport;

public class GetTransportBoxesHandlerTests
{
    private readonly Mock<ITransportBoxRepository> _repositoryMock;
    private readonly Mock<ILogger<GetTransportBoxesHandler>> _loggerMock;
    private readonly GetTransportBoxesHandler _handler;

    public GetTransportBoxesHandlerTests()
    {
        _repositoryMock = new Mock<ITransportBoxRepository>();
        _loggerMock = new Mock<ILogger<GetTransportBoxesHandler>>();

        var config = new MapperConfiguration(cfg =>
        {
            cfg.AddProfile<TransportBoxMappingProfile>();
        }, NullLoggerFactory.Instance);
        var mapper = config.CreateMapper();

        _handler = new GetTransportBoxesHandler(_loggerMock.Object, _repositoryMock.Object, mapper);
    }

    [Theory]
    [InlineData("ACTIVE", null, true)]
    [InlineData("active", null, true)]
    [InlineData("Opened", TransportBoxState.Opened, false)]
    [InlineData("closed", TransportBoxState.Closed, false)]
    [InlineData(null, null, false)]
    [InlineData("", null, false)]
    [InlineData("   ", null, false)]
    [InlineData("NotARealState", null, false)]
    public async Task Handle_StateFilter_RoutesExpectedArgumentsToRepository(
        string? state, TransportBoxState? expectedState, bool expectedIsActiveFilter)
    {
        // Arrange
        _repositoryMock
            .Setup(x => x.GetPagedListAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<TransportBoxState?>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync((new List<TransportBox>(), 0));

        var request = new GetTransportBoxesRequest { State = state };

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _repositoryMock.Verify(x => x.GetPagedListAsync(
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<string>(),
            expectedState,
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            expectedIsActiveFilter),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ForwardsAllPassThroughParametersToRepository()
    {
        // Arrange
        _repositoryMock
            .Setup(x => x.GetPagedListAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<TransportBoxState?>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync((new List<TransportBox>(), 0));

        var request = new GetTransportBoxesRequest
        {
            Skip = 20,
            Take = 10,
            Code = "B001",
            ProductCode = "P123",
            SortBy = "Code",
            SortDescending = true
        };

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _repositoryMock.Verify(x => x.GetPagedListAsync(
            20,
            10,
            "B001",
            null,
            "P123",
            "Code",
            true,
            false),
            Times.Once);
    }

    [Fact]
    public async Task Handle_MapsRepositoryResultIntoResponse()
    {
        // Arrange — use public API to build realistic TransportBox instances (mirrors GetTransportBoxByIdHandlerTests.cs)
        var box1 = new TransportBox();
        box1.Open("B001", DateTime.UtcNow, "user");

        var box2 = new TransportBox();
        box2.Open("B002", DateTime.UtcNow, "user");

        var items = new List<TransportBox> { box1, box2 };
        const int totalCount = 25; // distinct from items.Count (2), simulating a paged scenario

        _repositoryMock
            .Setup(x => x.GetPagedListAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<TransportBoxState?>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync((items, totalCount));

        var request = new GetTransportBoxesRequest { Skip = 10, Take = 2 };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Items.Should().HaveCount(2);
        result.Items[0].Code.Should().Be("B001");
        result.Items[1].Code.Should().Be("B002");
        result.Items[0].State.Should().Be(nameof(TransportBoxState.Opened));
        result.TotalCount.Should().Be(totalCount);
        result.Skip.Should().Be(10);
        result.Take.Should().Be(2);
    }
}
