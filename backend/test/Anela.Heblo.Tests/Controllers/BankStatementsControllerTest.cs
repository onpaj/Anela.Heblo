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

public class BankStatementsControllerTest : IClassFixture<ManufactureOrderTestFactory>
{
    private readonly ManufactureOrderTestFactory _factory;

    public BankStatementsControllerTest(ManufactureOrderTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetStatistics_Should_Return_Success_With_Data()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var controller = new BankStatementsController(mediator);

        var startDate = DateTime.Today.AddDays(-30);
        var endDate = DateTime.Today;

        // Act
        var result = await controller.GetStatistics(startDate, endDate);

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
    public async Task GetStatistics_Should_Accept_Null_Dates()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var controller = new BankStatementsController(mediator);

        // Act
        var result = await controller.GetStatistics();

        // Assert
        result.Should().NotBeNull();
        result.Result.Should().BeOfType<OkObjectResult>();
        
        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.Value.Should().BeOfType<GetBankStatementImportStatisticsResponse>();
    }

    [Fact]
    public async Task GetStatistics_Should_Pass_Correct_Parameters_To_Handler()
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

        var controller = new BankStatementsController(mockMediator.Object);
        var startDate = DateTime.Today.AddDays(-7);
        var endDate = DateTime.Today;

        // Act
        var result = await controller.GetStatistics(startDate, endDate);

        // Assert
        mockMediator.Verify(
            m => m.Send(
                It.Is<GetBankStatementImportStatisticsRequest>(r => 
                    r.StartDate == startDate && r.EndDate == endDate),
                It.IsAny<CancellationToken>()),
            Times.Once);

        result.Should().NotBeNull();
        result.Result.Should().BeOfType<OkObjectResult>();
        
        var okResult = result.Result as OkObjectResult;
        okResult!.Value.Should().Be(expectedResponse);
    }
}