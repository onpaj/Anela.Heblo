using Anela.Heblo.Application.Features.PackingMaterials.Services;
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.ProcessDailyConsumption;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.PackingMaterials;

public class ProcessDailyConsumptionHandlerTests
{
    private static readonly DateOnly TestDate = new(2026, 1, 15);

    private static (
        ProcessDailyConsumptionHandler Sut,
        Mock<IConsumptionCalculationService> Service,
        Mock<ILogger<ProcessDailyConsumptionHandler>> Logger)
        MakeSut()
    {
        var service = new Mock<IConsumptionCalculationService>();
        var logger = new Mock<ILogger<ProcessDailyConsumptionHandler>>();
        var sut = new ProcessDailyConsumptionHandler(service.Object, logger.Object);
        return (sut, service, logger);
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenAlreadyProcessed()
    {
        // Arrange
        var (sut, service, _) = MakeSut();
        // MaterialsProcessed=42 on the service result proves the handler ignores it
        // when WasRun=false (it must force MaterialsProcessed to 0 in the response).
        service
            .Setup(s => s.ProcessDailyConsumptionAsync(TestDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessDailyConsumptionResult(WasRun: false, MaterialsProcessed: 42));

        var request = new ProcessDailyConsumptionRequest { ProcessingDate = TestDate };

        // Act
        var response = await sut.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.MaterialsProcessed.Should().Be(0);
        response.ProcessedDate.Should().Be(TestDate);
        response.Message.Should().Contain("already processed");
        response.Message.Should().Contain(TestDate.ToString());

        service.Verify(
            s => s.ProcessDailyConsumptionAsync(TestDate, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsSuccess_WhenMaterialsUpdated()
    {
        // Arrange
        const int materialsUpdated = 5;
        var (sut, service, _) = MakeSut();
        service
            .Setup(s => s.ProcessDailyConsumptionAsync(TestDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessDailyConsumptionResult(WasRun: true, MaterialsProcessed: materialsUpdated));

        var request = new ProcessDailyConsumptionRequest { ProcessingDate = TestDate };

        // Act
        var response = await sut.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.MaterialsProcessed.Should().Be(materialsUpdated);
        response.ProcessedDate.Should().Be(TestDate);
        response.Message.Should().Contain(TestDate.ToString());
        response.Message.Should().Contain(materialsUpdated.ToString());
        response.Message.Should().Contain("materials updated");

        service.Verify(
            s => s.ProcessDailyConsumptionAsync(TestDate, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsSuccessWithZeroCount_WhenNoInvoicesFound()
    {
        // Arrange
        var (sut, service, _) = MakeSut();
        service
            .Setup(s => s.ProcessDailyConsumptionAsync(TestDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessDailyConsumptionResult(WasRun: true, MaterialsProcessed: 0));

        var request = new ProcessDailyConsumptionRequest { ProcessingDate = TestDate };

        // Act
        var response = await sut.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.MaterialsProcessed.Should().Be(0);
        response.ProcessedDate.Should().Be(TestDate);
        response.Message.Should().Contain("No invoices");
        response.Message.Should().Contain(TestDate.ToString());

        service.Verify(
            s => s.ProcessDailyConsumptionAsync(TestDate, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsGenericError_WhenServiceThrows()
    {
        // Arrange
        const string secretDetail = "secret database connection string";
        var thrown = new InvalidOperationException(secretDetail);

        var (sut, service, logger) = MakeSut();
        service
            .Setup(s => s.ProcessDailyConsumptionAsync(TestDate, It.IsAny<CancellationToken>()))
            .ThrowsAsync(thrown);

        var request = new ProcessDailyConsumptionRequest { ProcessingDate = TestDate };

        // Act
        var response = await sut.Handle(request, CancellationToken.None);

        // Assert — response shape
        response.Success.Should().BeFalse();
        response.MaterialsProcessed.Should().Be(0);
        response.ProcessedDate.Should().Be(TestDate);
        response.Message.Should().Be("An unexpected error occurred while processing daily consumption.");

        // Defense-in-depth: the secret must not leak into the message
        response.Message.Should().NotContain(secretDetail);
        response.Message.Should().NotContain(nameof(InvalidOperationException));

        // Logger contract: an Error-level entry was emitted with the same exception instance
        VerifyErrorLogged(logger, thrown);
    }

    private static void VerifyErrorLogged(
        Mock<ILogger<ProcessDailyConsumptionHandler>> logger,
        Exception expected)
    {
        logger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                expected,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
