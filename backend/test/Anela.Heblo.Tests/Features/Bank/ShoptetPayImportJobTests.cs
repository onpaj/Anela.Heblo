using Anela.Heblo.Application.Features.Bank.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.Bank.UseCases.ImportBankStatement;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Bank;

public class ShoptetPayImportJobTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IRecurringJobStatusChecker> _statusChecker = new();

    private ShoptetPayImportJob CreateJob()
    {
        _statusChecker
            .Setup(s => s.IsJobEnabledAsync("daily-shoptetpay-czk-import", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mediator
            .Setup(m => m.Send(It.IsAny<ImportBankStatementRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportBankStatementResponse { Statements = [] });

        return new ShoptetPayImportJob(
            _mediator.Object,
            NullLogger<ShoptetPayImportJob>.Instance,
            _statusChecker.Object);
    }

    [Fact]
    public async Task ExecuteAsync_SendsRequestWithCorrectAccountName_ShoptetPay()
    {
        var job = CreateJob();

        await job.ExecuteAsync();

        _mediator.Verify(m => m.Send(
            It.Is<ImportBankStatementRequest>(r => r.AccountName == "ShoptetPay-CZK"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenJobDisabled_DoesNotCallMediator()
    {
        _statusChecker
            .Setup(s => s.IsJobEnabledAsync("daily-shoptetpay-czk-import", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var job = new ShoptetPayImportJob(
            _mediator.Object,
            NullLogger<ShoptetPayImportJob>.Instance,
            _statusChecker.Object);

        await job.ExecuteAsync();

        _mediator.Verify(m => m.Send(It.IsAny<ImportBankStatementRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
