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

public sealed class ComgateEurImportJobTests
{
    [Fact]
    public void Metadata_MatchesContractValues()
    {
        var job = CreateJob();

        job.Metadata.JobName.Should().Be("daily-comgate-eur-import");
        job.Metadata.DisplayName.Should().Be("Daily Comgate EUR Import");
        job.Metadata.Description.Should().Be("Imports Comgate EUR payment statements from previous day");
        job.Metadata.CronExpression.Should().Be("40 4 * * *");
        job.Metadata.DefaultIsEnabled.Should().BeTrue();
    }

    [Fact]
    public void AccountName_IsComgateEur_WireContractStable()
    {
        GetAccountName(CreateJob()).Should().Be(BankAccountNames.ComgateEur).And.Be("ComgateEUR");
    }

    [Fact]
    public void GetTargetEndDate_ReturnsYesterday()
    {
        var today = new DateTime(2026, 6, 15);
        InvokeGetTargetEndDate(CreateJob(), today).Should().Be(new DateTime(2026, 6, 14));
    }

    private static ComgateEurImportJob CreateJob() => new(
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
