using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetStockAnalysis;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Tests.Controllers;

public class ManufacturingStockAnalysisControllerTests
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly ManufacturingStockAnalysisController _controller;

    public ManufacturingStockAnalysisControllerTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _controller = new ManufacturingStockAnalysisController(_mediatorMock.Object);

        // Setup HttpContext for BaseApiController.Logger
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        var serviceProvider = serviceCollection.BuildServiceProvider();

        var httpContext = new DefaultHttpContext();
        httpContext.RequestServices = serviceProvider;

        _controller.ControllerContext = new ControllerContext()
        {
            HttpContext = httpContext
        };
    }

    [Fact]
    public async Task GetStockAnalysis_WithDefaultRequest_ReturnsOk()
    {
        // Arrange
        var expectedResponse = new GetManufacturingStockAnalysisResponse
        {
            Items = new List<ManufacturingStockItemDto>(),
            Summary = new ManufacturingStockSummaryDto
            {
                TotalProducts = 0,
                CriticalCount = 0,
                MajorCount = 0,
                MinorCount = 0,
                AdequateCount = 0,
                UnconfiguredCount = 0,
                AnalysisPeriodStart = DateTime.UtcNow.AddDays(-90),
                AnalysisPeriodEnd = DateTime.UtcNow
            },
            TotalCount = 0,
            PageNumber = 1,
            PageSize = 20
        };

        _mediatorMock.Setup(m => m.Send(It.IsAny<GetManufacturingStockAnalysisRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.GetStockAnalysis(new GetManufacturingStockAnalysisRequest());

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>();
        var response = okResult.Subject.Value.Should().BeOfType<GetManufacturingStockAnalysisResponse>();
        response.Subject.Items.Should().NotBeNull();
        response.Subject.Summary.Should().NotBeNull();
    }

    [Fact]
    public async Task GetStockAnalysis_WithTimePeriodFilter_SendsCorrectRequest()
    {
        // Arrange
        var request = new GetManufacturingStockAnalysisRequest
        {
            TimePeriod = TimePeriodFilter.PreviousQuarter,
            PageSize = 10,
            PageNumber = 1
        };

        var expectedResponse = new GetManufacturingStockAnalysisResponse
        {
            Items = new List<ManufacturingStockItemDto>(),
            Summary = new ManufacturingStockSummaryDto
            {
                TotalProducts = 0,
                CriticalCount = 0,
                MajorCount = 0,
                MinorCount = 0,
                AdequateCount = 0,
                UnconfiguredCount = 0,
                AnalysisPeriodStart = DateTime.UtcNow.AddMonths(-3),
                AnalysisPeriodEnd = DateTime.UtcNow
            },
            TotalCount = 0,
            PageNumber = 1,
            PageSize = 10
        };

        _mediatorMock.Setup(m => m.Send(It.Is<GetManufacturingStockAnalysisRequest>(
                r => r.TimePeriod == TimePeriodFilter.PreviousQuarter &&
                     r.PageSize == 10 &&
                     r.PageNumber == 1),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.GetStockAnalysis(request);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>();
        var response = okResult.Subject.Value.Should().BeOfType<GetManufacturingStockAnalysisResponse>();
        response.Subject.PageNumber.Should().Be(1);
        response.Subject.PageSize.Should().Be(10);
    }

    [Fact]
    public async Task GetStockAnalysis_WithCustomTimePeriod_SendsCorrectDates()
    {
        // Arrange
        var fromDate = DateTime.UtcNow.AddDays(-90);
        var toDate = DateTime.UtcNow.AddDays(-1);

        var request = new GetManufacturingStockAnalysisRequest
        {
            TimePeriod = TimePeriodFilter.CustomPeriod,
            CustomFromDate = fromDate,
            CustomToDate = toDate
        };

        var expectedResponse = new GetManufacturingStockAnalysisResponse
        {
            Items = new List<ManufacturingStockItemDto>(),
            Summary = new ManufacturingStockSummaryDto
            {
                TotalProducts = 0,
                CriticalCount = 0,
                MajorCount = 0,
                MinorCount = 0,
                AdequateCount = 0,
                UnconfiguredCount = 0,
                AnalysisPeriodStart = fromDate,
                AnalysisPeriodEnd = toDate
            },
            TotalCount = 0,
            PageNumber = 1,
            PageSize = 20
        };

        _mediatorMock.Setup(m => m.Send(It.Is<GetManufacturingStockAnalysisRequest>(
                r => r.TimePeriod == TimePeriodFilter.CustomPeriod &&
                     r.CustomFromDate == fromDate &&
                     r.CustomToDate == toDate),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.GetStockAnalysis(request);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>();
        var response = okResult.Subject.Value.Should().BeOfType<GetManufacturingStockAnalysisResponse>();
        response.Subject.Summary.AnalysisPeriodStart.Date.Should().Be(fromDate.Date);
        response.Subject.Summary.AnalysisPeriodEnd.Date.Should().Be(toDate.Date);
    }

    [Fact]
    public async Task GetStockAnalysis_WithCriticalItemsOnly_FiltersCorrectly()
    {
        // Arrange
        var request = new GetManufacturingStockAnalysisRequest
        {
            CriticalItemsOnly = true
        };

        var expectedResponse = new GetManufacturingStockAnalysisResponse
        {
            Items = new List<ManufacturingStockItemDto>
            {
                new ManufacturingStockItemDto
                {
                    Code = "TEST001",
                    Name = "Test Product",
                    Severity = ManufacturingStockSeverity.Critical,
                    CurrentStock = 5,
                    StockDaysAvailable = 3
                }
            },
            Summary = new ManufacturingStockSummaryDto
            {
                TotalProducts = 1,
                CriticalCount = 1,
                MajorCount = 0,
                MinorCount = 0,
                AdequateCount = 0,
                UnconfiguredCount = 0,
                AnalysisPeriodStart = DateTime.UtcNow.AddDays(-90),
                AnalysisPeriodEnd = DateTime.UtcNow
            },
            TotalCount = 1,
            PageNumber = 1,
            PageSize = 20
        };

        _mediatorMock.Setup(m => m.Send(It.Is<GetManufacturingStockAnalysisRequest>(
                r => r.CriticalItemsOnly == true),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.GetStockAnalysis(request);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>();
        var response = okResult.Subject.Value.Should().BeOfType<GetManufacturingStockAnalysisResponse>();
        response.Subject.Items.Should().HaveCount(1);
        Assert.All(response.Subject.Items, item => Assert.Equal(ManufacturingStockSeverity.Critical, item.Severity));
    }

    [Fact]
    public async Task GetStockAnalysis_WithSearchTerm_FiltersResults()
    {
        // Arrange
        var request = new GetManufacturingStockAnalysisRequest
        {
            SearchTerm = "test",
            PageSize = 100
        };

        var expectedResponse = new GetManufacturingStockAnalysisResponse
        {
            Items = new List<ManufacturingStockItemDto>
            {
                new ManufacturingStockItemDto { Code = "TEST001", Name = "Test Product 1" },
                new ManufacturingStockItemDto { Code = "PROD002", Name = "Another Test Item" }
            },
            Summary = new ManufacturingStockSummaryDto
            {
                TotalProducts = 2,
                CriticalCount = 0,
                MajorCount = 0,
                MinorCount = 0,
                AdequateCount = 2,
                UnconfiguredCount = 0,
                AnalysisPeriodStart = DateTime.UtcNow.AddDays(-90),
                AnalysisPeriodEnd = DateTime.UtcNow
            },
            TotalCount = 2,
            PageNumber = 1,
            PageSize = 100
        };

        _mediatorMock.Setup(m => m.Send(It.Is<GetManufacturingStockAnalysisRequest>(
                r => r.SearchTerm == "test" && r.PageSize == 100),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.GetStockAnalysis(request);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>();
        var response = okResult.Subject.Value.Should().BeOfType<GetManufacturingStockAnalysisResponse>();
        response.Subject.Items.Count.Should().Be(2);
    }

    [Fact]
    public async Task GetStockAnalysis_WithInvalidPageSize_ReturnsBadRequest()
    {
        // Arrange
        var request = new GetManufacturingStockAnalysisRequest
        {
            PageSize = 101 // Above maximum allowed
        };

        _controller.ModelState.AddModelError("PageSize", "The field PageSize must be between 1 and 100.");

        // Act
        var result = await _controller.GetStockAnalysis(request);

        // Assert
        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetStockAnalysis_WithUnconfiguredOnly_ShowsUnconfiguredItems()
    {
        // Arrange
        var request = new GetManufacturingStockAnalysisRequest
        {
            UnconfiguredOnly = true
        };

        var expectedResponse = new GetManufacturingStockAnalysisResponse
        {
            Items = new List<ManufacturingStockItemDto>
            {
                new ManufacturingStockItemDto
                {
                    Code = "UNCONFIGURED001",
                    Name = "Unconfigured Product",
                    Severity = ManufacturingStockSeverity.Unconfigured,
                    CurrentStock = 50,
                    IsConfigured = false
                }
            },
            Summary = new ManufacturingStockSummaryDto
            {
                TotalProducts = 1,
                CriticalCount = 0,
                MajorCount = 0,
                MinorCount = 0,
                AdequateCount = 0,
                UnconfiguredCount = 1,
                AnalysisPeriodStart = DateTime.UtcNow.AddDays(-90),
                AnalysisPeriodEnd = DateTime.UtcNow
            },
            TotalCount = 1,
            PageNumber = 1,
            PageSize = 20
        };

        _mediatorMock.Setup(m => m.Send(It.Is<GetManufacturingStockAnalysisRequest>(
                r => r.UnconfiguredOnly == true),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.GetStockAnalysis(request);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>();
        var response = okResult.Subject.Value.Should().BeOfType<GetManufacturingStockAnalysisResponse>();
        response.Subject.Items.Should().HaveCount(1);
        Assert.All(response.Subject.Items, item => Assert.Equal(ManufacturingStockSeverity.Unconfigured, item.Severity));
        Assert.All(response.Subject.Items, item => Assert.False(item.IsConfigured));
    }

    [Fact]
    public async Task GetStockAnalysis_ByDefault_HidesUnconfiguredItems()
    {
        // Arrange
        var request = new GetManufacturingStockAnalysisRequest
        {
            // Default request - should hide unconfigured items
            UnconfiguredOnly = false
        };

        var expectedResponse = new GetManufacturingStockAnalysisResponse
        {
            Items = new List<ManufacturingStockItemDto>
            {
                new ManufacturingStockItemDto
                {
                    Code = "CONFIGURED001",
                    Name = "Configured Product",
                    Severity = ManufacturingStockSeverity.Adequate,
                    CurrentStock = 100,
                    IsConfigured = true
                }
                // Note: Unconfigured items should NOT be included
            },
            Summary = new ManufacturingStockSummaryDto
            {
                TotalProducts = 1,
                CriticalCount = 0,
                MajorCount = 0,
                MinorCount = 0,
                AdequateCount = 1,
                UnconfiguredCount = 0, // Hidden from display
                AnalysisPeriodStart = DateTime.UtcNow.AddDays(-90),
                AnalysisPeriodEnd = DateTime.UtcNow
            },
            TotalCount = 1,
            PageNumber = 1,
            PageSize = 20
        };

        _mediatorMock.Setup(m => m.Send(It.Is<GetManufacturingStockAnalysisRequest>(
                r => r.UnconfiguredOnly == false),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.GetStockAnalysis(request);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>();
        var response = okResult.Subject.Value.Should().BeOfType<GetManufacturingStockAnalysisResponse>();
        response.Subject.Items.Should().HaveCount(1);
        Assert.All(response.Subject.Items, item => Assert.True(item.IsConfigured));
        Assert.All(response.Subject.Items, item => Assert.NotEqual(ManufacturingStockSeverity.Unconfigured, item.Severity));
    }

    [Fact]
    public async Task GetStockAnalysis_HandlerReturnsInvalidParametersError_ReturnsBadRequest()
    {
        // Arrange
        var errorResponse = new GetManufacturingStockAnalysisResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.InvalidAnalysisParameters,
            Params = new Dictionary<string, string> { ["parameters"] = "PageSize must be between 1 and 100" }
        };

        _mediatorMock.Setup(m => m.Send(It.IsAny<GetManufacturingStockAnalysisRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(errorResponse);

        // Act
        var result = await _controller.GetStockAnalysis(new GetManufacturingStockAnalysisRequest());

        // Assert
        var badRequestResult = result.Result.Should().BeOfType<BadRequestObjectResult>();
        badRequestResult.Subject.StatusCode.Should().Be(400); // BadRequest
        badRequestResult.Subject.Value.Should().Be(errorResponse);
    }

    [Fact]
    public async Task GetStockAnalysis_HandlerReturnsDataNotAvailableError_ReturnsNotFound()
    {
        // Arrange
        var errorResponse = new GetManufacturingStockAnalysisResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.ManufacturingDataNotAvailable,
            Params = new Dictionary<string, string> { ["reason"] = "No finished products available for analysis" }
        };

        _mediatorMock.Setup(m => m.Send(It.IsAny<GetManufacturingStockAnalysisRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(errorResponse);

        // Act
        var result = await _controller.GetStockAnalysis(new GetManufacturingStockAnalysisRequest());

        // Assert
        var notFoundResult = result.Result.Should().BeOfType<NotFoundObjectResult>();
        notFoundResult.Subject.StatusCode.Should().Be(404); // NotFound
        notFoundResult.Subject.Value.Should().Be(errorResponse);
    }
}