using Anela.Heblo.Adapters.Flexi.Invoices;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Rem.FlexiBeeSDK.Model.Invoices;
using IFlexiIssuedInvoiceClient = Rem.FlexiBeeSDK.Client.Clients.IssuedInvoices.IIssuedInvoiceClient;

namespace Anela.Heblo.Adapters.Flexi.Tests.Invoices;

public class FlexiIssuedInvoiceClientGetAllTests
{
    private readonly Mock<IFlexiIssuedInvoiceClient> _mockFlexiClient;
    private readonly FlexiIssuedInvoiceClient _client;

    public FlexiIssuedInvoiceClientGetAllTests()
    {
        _mockFlexiClient = new Mock<IFlexiIssuedInvoiceClient>();
        var mockMapper = new Mock<IMapper>();
        var mockLogger = new Mock<ILogger<FlexiIssuedInvoiceClient>>();

        _client = new FlexiIssuedInvoiceClient(
            _mockFlexiClient.Object,
            mockMapper.Object,
            mockLogger.Object);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsEmptyList_WhenFlexiReturnsNoInvoices()
    {
        // Arrange
        _mockFlexiClient
            .Setup(x => x.GetAllAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IssuedInvoiceDetailFlexiDto>().AsReadOnly());

        // Act
        var result = await _client.GetAllAsync(
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 1, 31),
            CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllAsync_MapsInvoiceCode_FromFlexiDto()
    {
        // Arrange
        var flexiInvoice = new IssuedInvoiceDetailFlexiDto
        {
            Code = "FAV-2024-001",
            SumTotal = "1210.00",
            Items = new List<IssuedInvoiceItemFlexiDto>(),
        };

        _mockFlexiClient
            .Setup(x => x.GetAllAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IssuedInvoiceDetailFlexiDto> { flexiInvoice }.AsReadOnly());

        // Act
        var result = await _client.GetAllAsync(
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 1, 31),
            CancellationToken.None);

        // Assert
        result.Should().HaveCount(1);
        result[0].Code.Should().Be("FAV-2024-001");
    }

    [Fact]
    public async Task GetAllAsync_MapsTotalWithVat_FromSumTotal()
    {
        // Arrange
        var flexiInvoice = new IssuedInvoiceDetailFlexiDto
        {
            Code = "FAV-2024-001",
            SumTotal = "1210.00",
            Items = new List<IssuedInvoiceItemFlexiDto>(),
        };

        _mockFlexiClient
            .Setup(x => x.GetAllAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IssuedInvoiceDetailFlexiDto> { flexiInvoice }.AsReadOnly());

        // Act
        var result = await _client.GetAllAsync(
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 1, 31),
            CancellationToken.None);

        // Assert
        result[0].Price.TotalWithVat.Should().Be(1210.00m);
    }

    [Fact]
    public async Task GetAllAsync_MapsTotalWithoutVat_AsSumOfItemSumBase()
    {
        // Arrange
        var flexiInvoice = new IssuedInvoiceDetailFlexiDto
        {
            Code = "FAV-2024-001",
            SumTotal = "1210.00",
            Items = new List<IssuedInvoiceItemFlexiDto>
            {
                new() { Code = "PROD-A", Amount = "2", PricePerUnit = 500m, SumBase = 1000m, SumTotal = 1210m },
            },
        };

        _mockFlexiClient
            .Setup(x => x.GetAllAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IssuedInvoiceDetailFlexiDto> { flexiInvoice }.AsReadOnly());

        // Act
        var result = await _client.GetAllAsync(
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 1, 31),
            CancellationToken.None);

        // Assert
        result[0].Price.TotalWithoutVat.Should().Be(1000m);
    }

    [Fact]
    public async Task GetAllAsync_MapsItems_WithCodeAmountAndPrices()
    {
        // Arrange
        var flexiInvoice = new IssuedInvoiceDetailFlexiDto
        {
            Code = "FAV-2024-001",
            SumTotal = "363.00",
            Items = new List<IssuedInvoiceItemFlexiDto>
            {
                new()
                {
                    Code = "PROD-001",
                    Name = "Test Product",
                    Amount = "3",
                    PricePerUnit = 100m,
                    SumBase = 300m,
                    SumTotal = 363m,
                },
            },
        };

        _mockFlexiClient
            .Setup(x => x.GetAllAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IssuedInvoiceDetailFlexiDto> { flexiInvoice }.AsReadOnly());

        // Act
        var result = await _client.GetAllAsync(
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 1, 31),
            CancellationToken.None);

        // Assert
        var item = result[0].Items.Should().HaveCount(1).And.Subject.First();
        item.Code.Should().Be("PROD-001");
        item.Amount.Should().Be(3m);
        item.ItemPrice.WithoutVat.Should().Be(100m);
        item.ItemPrice.TotalWithoutVat.Should().Be(300m);
        item.ItemPrice.TotalWithVat.Should().Be(363m);
        item.ItemPrice.WithVat.Should().Be(121m); // 363 / 3
    }

    [Fact]
    public async Task GetAllAsync_UsesSumTotalC_WhenSumTotalIsNull()
    {
        // Arrange — foreign-currency invoice item: SumTotal is null, SumTotalC has the value
        var flexiInvoice = new IssuedInvoiceDetailFlexiDto
        {
            Code = "FAV-2024-EUR",
            SumTotal = "0",
            Items = new List<IssuedInvoiceItemFlexiDto>
            {
                new()
                {
                    Code = "PROD-002",
                    Amount = "1",
                    PricePerUnit = 50m,
                    SumTotal = null,
                    SumTotalC = 50m,
                    SumBase = null,
                    SumBaseC = 41.32m,
                },
            },
        };

        _mockFlexiClient
            .Setup(x => x.GetAllAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IssuedInvoiceDetailFlexiDto> { flexiInvoice }.AsReadOnly());

        // Act
        var result = await _client.GetAllAsync(
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 1, 31),
            CancellationToken.None);

        // Assert
        var item = result[0].Items[0];
        item.ItemPrice.TotalWithVat.Should().Be(50m);
        item.ItemPrice.TotalWithoutVat.Should().Be(41.32m);
    }

    [Fact]
    public async Task GetAllAsync_PassesCorrectDateRange_ToSdkClient()
    {
        // Arrange
        var from = new DateOnly(2024, 3, 1);
        var to = new DateOnly(2024, 3, 31);

        DateTime? capturedFrom = null;
        DateTime? capturedTo = null;

        _mockFlexiClient
            .Setup(x => x.GetAllAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback<DateTime, DateTime, CancellationToken>((df, dt, _) =>
            {
                capturedFrom = df;
                capturedTo = dt;
            })
            .ReturnsAsync(new List<IssuedInvoiceDetailFlexiDto>().AsReadOnly());

        // Act
        await _client.GetAllAsync(from, to, CancellationToken.None);

        // Assert — from = start of day, to = end of day (full last day included)
        capturedFrom.Should().Be(new DateTime(2024, 3, 1, 0, 0, 0));
        capturedTo!.Value.Date.Should().Be(new DateTime(2024, 3, 31));
        capturedTo.Value.TimeOfDay.Should().BeGreaterThan(TimeSpan.FromHours(23));
    }

    [Fact]
    public async Task GetAllAsync_LogsAndRethrows_WhenSdkThrows()
    {
        // Arrange
        var exception = new InvalidOperationException("FlexiBee connection failed");

        _mockFlexiClient
            .Setup(x => x.GetAllAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        // Act
        var act = async () => await _client.GetAllAsync(
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 1, 31),
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("FlexiBee connection failed");
    }

    [Fact]
    public async Task GetAllAsync_HandlesNullItemCode_AsEmptyString()
    {
        // Arrange
        var flexiInvoice = new IssuedInvoiceDetailFlexiDto
        {
            Code = "FAV-2024-001",
            SumTotal = "121",
            Items = new List<IssuedInvoiceItemFlexiDto>
            {
                new() { Code = null, Amount = "1", PricePerUnit = 100m, SumBase = 100m, SumTotal = 121m },
            },
        };

        _mockFlexiClient
            .Setup(x => x.GetAllAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IssuedInvoiceDetailFlexiDto> { flexiInvoice }.AsReadOnly());

        // Act
        var result = await _client.GetAllAsync(
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 1, 31),
            CancellationToken.None);

        // Assert
        result[0].Items[0].Code.Should().Be(string.Empty);
    }
}
