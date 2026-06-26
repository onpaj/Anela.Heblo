using System.Reflection;
using Anela.Heblo.Application.Features.Bank;
using Anela.Heblo.Application.Features.Bank.Infrastructure.Jobs;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Bank;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
    public void AccountName_IsShoptetPayCzk_WireContractStable()
    {
        GetAccountName(CreateJob()).Should().Be(BankAccountNames.ShoptetPayCzk).And.Be("ShoptetPay-CZK");
    }

    [Fact]
    public void GetTargetEndDate_ReturnsToday()
    {
        var today = new DateTime(2026, 6, 15);
        InvokeGetTargetEndDate(CreateJob(), today).Should().Be(new DateTime(2026, 6, 15));
    }

    private static ShoptetPayImportJob CreateJob() => new(
        Mock.Of<IMediator>(),
        NullLoggerFactory.Instance,
        Mock.Of<IRecurringJobStatusChecker>(),
        Mock.Of<IBankImportStateRepository>(),
        Mock.Of<IBankStatementImportRepository>(),
        Options.Create(new BankImportWatermarkOptions()));

    private static string GetAccountName(BankImportJobBase job) =>
        (string)typeof(BankImportJobBase)
            .GetProperty("AccountName", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(job)!;

    private static DateTime InvokeGetTargetEndDate(BankImportJobBase job, DateTime today) =>
        (DateTime)typeof(BankImportJobBase)
            .GetMethod("GetTargetEndDate", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(job, new object[] { today })!;
}
