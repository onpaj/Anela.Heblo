using System.Reflection;
using Anela.Heblo.Application.Features.Bank;
using Anela.Heblo.Application.Features.Bank.Infrastructure.Jobs;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Bank.Infrastructure.Jobs;

public sealed class ShoptetPayImportJobTests
{
    [Fact]
    public void Metadata_MatchesContractValues()
    {
        var job = CreateJob();

        job.Metadata.JobName.Should().Be("daily-shoptetpay-czk-import");
        job.Metadata.DisplayName.Should().Be("Daily ShoptetPay CZK Import");
        job.Metadata.Description.Should().Be("Imports ShoptetPay CZK payment statements from current day");
        job.Metadata.CronExpression.Should().Be("50 4 * * *");
        job.Metadata.DefaultIsEnabled.Should().BeTrue();
    }

    [Fact]
    public void GetParameters_ReturnsShoptetPayCzkAccount_WithTodayDateRange()
    {
        var job = CreateJob();
        var today = DateTime.Today;

        var parameters = InvokeGetParameters(job);

        parameters.AccountName.Should().Be(BankAccountNames.ShoptetPayCzk);
        // Guard against accidental rename of the constant value — the wire
        // contract with BankAccountSettings.Accounts[].Name must stay stable.
        parameters.AccountName.Should().Be("ShoptetPay-CZK");
        parameters.DateFrom.Should().Be(parameters.DateTo);
        parameters.DateFrom.Should().BeOnOrAfter(today.AddDays(-1));
        parameters.DateFrom.Should().BeOnOrBefore(today.AddDays(1));
    }

    private static ShoptetPayImportJob CreateJob() => new(
        Mock.Of<IMediator>(),
        NullLoggerFactory.Instance,
        Mock.Of<IRecurringJobStatusChecker>());

    private static BankImportJobParameters InvokeGetParameters(ShoptetPayImportJob job)
    {
        var method = typeof(ShoptetPayImportJob).GetMethod(
            "GetParameters",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (BankImportJobParameters)method.Invoke(job, null)!;
    }
}
