using Anela.Heblo.Adapters.Flexi.Bank;
using Anela.Heblo.Domain.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Rem.FlexiBeeSDK.Client.Clients.BankAccounts;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Bank;

public class FlexiBankStatementImportServiceTests
{
    private readonly Mock<FlexiBankAccountClient> _mockFlexiBankAccountClient;
    private readonly FlexiBankStatementImportService _sut;

    public FlexiBankStatementImportServiceTests()
    {
        _mockFlexiBankAccountClient = new Mock<FlexiBankAccountClient>(
            Mock.Of<IBankAccountClient>(),
            Mock.Of<ILogger<FlexiBankAccountClient>>());

        _sut = new FlexiBankStatementImportService(
            _mockFlexiBankAccountClient.Object,
            Mock.Of<ILogger<FlexiBankStatementImportService>>());
    }

    [Fact]
    public async Task ImportStatementAsync_WhenClientReturnsSuccess_ReturnsSuccessResult()
    {
        _mockFlexiBankAccountClient
            .Setup(x => x.ImportStatementAsync(It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(Result.Success(true));

        var result = await _sut.ImportStatementAsync(1, "statement-data");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task ImportStatementAsync_WhenClientReturnsFailureWithMessage_ReturnsSameFailureMessage()
    {
        _mockFlexiBankAccountClient
            .Setup(x => x.ImportStatementAsync(It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(Result.Failure<bool>("some FlexiBee error"));

        var result = await _sut.ImportStatementAsync(1, "statement-data");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("some FlexiBee error");
    }

    [Fact]
    public async Task ImportStatementAsync_WhenClientReturnsFailureWithNullMessage_FallsBackToUnknownImportError()
    {
        _mockFlexiBankAccountClient
            .Setup(x => x.ImportStatementAsync(It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(Result.Failure<bool>(null!));

        var result = await _sut.ImportStatementAsync(1, "statement-data");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("Unknown import error");
    }

    [Fact]
    public async Task ImportStatementAsync_WhenClientThrows_ReturnsFailureWithExceptionMessageAndDoesNotThrow()
    {
        _mockFlexiBankAccountClient
            .Setup(x => x.ImportStatementAsync(It.IsAny<int>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var result = await _sut.ImportStatementAsync(1, "statement-data");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("Exception during import: boom");
    }
}
