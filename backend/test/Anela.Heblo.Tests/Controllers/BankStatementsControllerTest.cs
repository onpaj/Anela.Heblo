using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementImportStatistics;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Controllers;

public class AnalyticsControllerBankStatementsTest : IClassFixture<ManufactureOrderTestFactory>
{
    private readonly ManufactureOrderTestFactory _factory;

    public AnalyticsControllerBankStatementsTest(ManufactureOrderTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetBankStatementImportStatistics_Should_Return_Success_With_Data()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var controller = new AnalyticsController(mediator);

        var request = new GetBankStatementImportStatisticsRequest
        {
            StartDate = DateTime.Today.AddDays(-30),
            EndDate = DateTime.Today
        };

        // Act
        var result = await controller.GetBankStatementImportStatistics(request);

        // Assert
        result.Should().NotBeNull();
        result.Result.Should().BeOfType<OkObjectResult>();

        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.Value.Should().BeOfType<GetBankStatementImportStatisticsResponse>();

        var response = okResult.Value as GetBankStatementImportStatisticsResponse;
        response.Should().NotBeNull();
        response!.Statistics.Should().NotBeNull();
    }

    [Fact]
    public async Task GetBankStatementImportStatistics_Should_Accept_Null_Dates()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var controller = new AnalyticsController(mediator);

        var request = new GetBankStatementImportStatisticsRequest();

        // Act
        var result = await controller.GetBankStatementImportStatistics(request);

        // Assert
        result.Should().NotBeNull();
        result.Result.Should().BeOfType<OkObjectResult>();

        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.Value.Should().BeOfType<GetBankStatementImportStatisticsResponse>();
    }

    [Fact]
    public async Task GetBankStatementImportStatistics_Should_Pass_Correct_Parameters_To_Handler()
    {
        // Arrange
        var mockMediator = new Mock<IMediator>();
        var expectedResponse = new GetBankStatementImportStatisticsResponse
        {
            Statistics = new List<BankStatementImportStatisticsDto>
            {
                new BankStatementImportStatisticsDto
                {
                    Date = DateTime.Today,
                    ImportCount = 5,
                    TotalItemCount = 25
                }
            }
        };

        mockMediator
            .Setup(m => m.Send(It.IsAny<GetBankStatementImportStatisticsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        var controller = new AnalyticsController(mockMediator.Object);
        var request = new GetBankStatementImportStatisticsRequest
        {
            StartDate = DateTime.Today.AddDays(-7),
            EndDate = DateTime.Today
        };

        // Act
        var result = await controller.GetBankStatementImportStatistics(request);

        // Assert
        mockMediator.Verify(
            m => m.Send(
                It.Is<GetBankStatementImportStatisticsRequest>(r =>
                    r.StartDate == request.StartDate && r.EndDate == request.EndDate),
                It.IsAny<CancellationToken>()),
            Times.Once);

        result.Should().NotBeNull();
        result.Result.Should().BeOfType<OkObjectResult>();

        var okResult = result.Result as OkObjectResult;
        okResult!.Value.Should().Be(expectedResponse);
    }
}