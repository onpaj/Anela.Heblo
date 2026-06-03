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

public sealed class ComgateCzkImportJobTests
{
    [Fact]
    public void Metadata_MatchesContractValues()
    {
        var job = CreateJob();

        job.Metadata.JobName.Should().Be("daily-comgate-czk-import");
        job.Metadata.DisplayName.Should().Be("Daily Comgate CZK Import");
        job.Metadata.Description.Should().Be("Imports Comgate CZK payment statements from previous day");
        job.Metadata.CronExpression.Should().Be("30 4 * * *");
        job.Metadata.DefaultIsEnabled.Should().BeTrue();
    }

    [Fact]
    public void GetParameters_ReturnsComgateCzkAccount_WithYesterdayDateRange()
    {
        var job = CreateJob();
        var beforeYesterday = DateTime.Today.AddDays(-1);

        var parameters = InvokeGetParameters(job);

        parameters.AccountName.Should().Be(BankAccountNames.ComgateCzk);
        // Guard against accidental rename of the constant value — the wire
        // contract with BankAccountSettings.Accounts[].Name must stay stable.
        parameters.AccountName.Should().Be("ComgateCZK");
        parameters.DateFrom.Should().Be(parameters.DateTo);
        // ±1 day tolerance to remain deterministic across midnight rollover.
        parameters.DateFrom.Should().BeOnOrAfter(beforeYesterday.AddDays(-1));
        parameters.DateFrom.Should().BeOnOrBefore(beforeYesterday.AddDays(1));
    }

    private static ComgateCzkImportJob CreateJob() => new(
        Mock.Of<IMediator>(),
        NullLoggerFactory.Instance,
        Mock.Of<IRecurringJobStatusChecker>());

    private static BankImportJobParameters InvokeGetParameters(ComgateCzkImportJob job)
    {
        var method = typeof(ComgateCzkImportJob).GetMethod(
            "GetParameters",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (BankImportJobParameters)method.Invoke(job, null)!;
    }
}
