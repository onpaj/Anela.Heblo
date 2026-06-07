using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.Bank.Contracts;
using Anela.Heblo.Application.Features.Bank.UseCases.GetBankAccounts;
using Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementById;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Controllers;

public class BankStatementsControllerTests
{
    private readonly Mock<IMediator> _mockMediator;
    private readonly Mock<ILogger<BankStatementsController>> _mockLogger;
    private readonly BankStatementsController _controller;

    public BankStatementsControllerTests()
    {
        _mockMediator = new Mock<IMediator>();
        _mockLogger = new Mock<ILogger<BankStatementsController>>();
        _controller = new BankStatementsController(_mockMediator.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task GetAccounts_DispatchesGetBankAccountsRequest()
    {
        _mockMediator
            .Setup(m => m.Send(It.IsAny<GetBankAccountsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetBankAccountsResponse());

        await _controller.GetAccounts(CancellationToken.None);

        _mockMediator.Verify(
            m => m.Send(It.IsAny<GetBankAccountsRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetAccounts_ReturnsOkWithBareArrayOfDtos_NotEnvelope()
    {
        var accounts = new List<BankAccountDto>
        {
            new BankAccountDto
            {
                Name = "ComgateCZK",
                AccountNumber = "123456789",
                Provider = "Comgate",
                Currency = "CZK",
            }
        };
        _mockMediator
            .Setup(m => m.Send(It.IsAny<GetBankAccountsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetBankAccountsResponse { Accounts = accounts });

        var actionResult = await _controller.GetAccounts(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        var payload = Assert.IsAssignableFrom<IEnumerable<BankAccountDto>>(ok.Value);
        Assert.NotSame(typeof(GetBankAccountsResponse), ok.Value!.GetType());
        Assert.Single(payload);
        Assert.Equal("ComgateCZK", payload.First().Name);
    }

    [Fact]
    public async Task GetAccounts_WithEmptyHandlerResponse_ReturnsOkWithEmptyArray()
    {
        _mockMediator
            .Setup(m => m.Send(It.IsAny<GetBankAccountsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetBankAccountsResponse());

        var actionResult = await _controller.GetAccounts(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        var payload = Assert.IsAssignableFrom<IEnumerable<BankAccountDto>>(ok.Value);
        Assert.Empty(payload);
    }

    [Fact]
    public async Task GetBankStatement_WithExistingId_DispatchesByIdRequestAndReturnsOk()
    {
        // Arrange
        var dto = new BankStatementImportDto
        {
            Id = 42,
            TransferId = "T-EXISTS",
            StatementDate = new DateTime(2026, 1, 15),
            ImportDate = new DateTime(2026, 1, 16),
            Account = "123456789",
            Currency = "CZK",
            ItemCount = 5,
            ImportResult = "OK"
        };
        _mockMediator
            .Setup(m => m.Send(It.Is<GetBankStatementByIdRequest>(r => r.Id == 42), It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        // Act
        var actionResult = await _controller.GetBankStatement(42, CancellationToken.None);

        // Assert
        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        var payload = Assert.IsType<BankStatementImportDto>(ok.Value);
        Assert.Equal(42, payload.Id);
        Assert.Equal("T-EXISTS", payload.TransferId);

        _mockMediator.Verify(
            m => m.Send(It.Is<GetBankStatementByIdRequest>(r => r.Id == 42), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetBankStatement_WithMissingId_ReturnsNotFoundWithMessageBody()
    {
        // Arrange
        _mockMediator
            .Setup(m => m.Send(It.IsAny<GetBankStatementByIdRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BankStatementImportDto?)null);

        // Act
        var actionResult = await _controller.GetBankStatement(99999, CancellationToken.None);

        // Assert
        var notFound = Assert.IsType<NotFoundObjectResult>(actionResult.Result);
        Assert.NotNull(notFound.Value);

        // Wire-format guarantee: the 404 body must keep the { message = "..." } shape.
        var messageProp = notFound.Value!.GetType().GetProperty("message");
        Assert.NotNull(messageProp);
        var messageValue = messageProp!.GetValue(notFound.Value) as string;
        Assert.Equal("Bank statement import with ID 99999 not found", messageValue);
    }

    [Fact]
    public async Task GetBankStatement_DoesNotDispatchListRequest()
    {
        // Arrange — guarantees the old list-handler workaround is gone.
        _mockMediator
            .Setup(m => m.Send(It.IsAny<GetBankStatementByIdRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BankStatementImportDto?)null);

        // Act
        await _controller.GetBankStatement(7, CancellationToken.None);

        // Assert — only ByIdRequest was sent; no ListRequest was constructed.
        _mockMediator.Verify(
            m => m.Send(It.IsAny<GetBankStatementByIdRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _mockMediator.Verify(
            m => m.Send(It.IsAny<Anela.Heblo.Application.Features.Bank.UseCases.GetBankStatementList.GetBankStatementListRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
