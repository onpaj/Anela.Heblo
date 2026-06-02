using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.Bank.Contracts;
using Anela.Heblo.Application.Features.Bank.UseCases.GetBankAccounts;
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
}
