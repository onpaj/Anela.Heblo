using System.Collections.Concurrent;
using Anela.Heblo.Adapters.Flexi.Accounting.Ledger;
using Anela.Heblo.Application.Features.FinancialOverview;
using Anela.Heblo.Application.Features.FinancialOverview.Services;
using Anela.Heblo.Domain.Accounting.Ledger;
using Anela.Heblo.Domain.Features.FinancialOverview;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Rem.FlexiBeeSDK.Client.Clients.Accounting.Ledger;
using Rem.FlexiBeeSDK.Model.Accounting.Ledger;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Accounting;

/// <summary>
/// FR-5: a real LedgerService over a counting fake ILedgerClient must never issue more FlexiBee
/// requests than there are distinct (window, prefix, department) tuples, even when several
/// FinancialAnalysisService runs overlap. Lives in the Flexi test project because it needs the
/// real LedgerService (Flexi adapter) together with the Application-layer FinancialAnalysisService.
/// </summary>
public class LedgerServiceFinancialAnalysisTests
{
    [Fact]
    public async Task OverlappingFinancialAnalysisRuns_CallClientAtMostOncePerDistinctTuple()
    {
        var calls = new ConcurrentBag<string>();
        var client = new Mock<ILedgerClient>();
        client
            .Setup(x => x.GetAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(),
                It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(async (DateTime from, DateTime to, IEnumerable<string> debit, IEnumerable<string> credit,
                string? dept, int _, int __, CancellationToken ___) =>
            {
                calls.Add($"{from:yyyyMMdd}|{to:yyyyMMdd}|{string.Join(",", debit)}|{string.Join(",", credit)}|{dept}");
                await Task.Delay(50);
                return (IReadOnlyList<LedgerItemFlexiDto>)new List<LedgerItemFlexiDto>();
            });

        var mapper = new Mock<IMapper>();
        mapper.Setup(x => x.Map<List<LedgerItem>>(It.IsAny<object>())).Returns(() => new List<LedgerItem>());

        var ledger = new LedgerService(
            client.Object,
            new MemoryCache(new MemoryCacheOptions()),
            mapper.Object,
            NullLogger<LedgerService>.Instance,
            Options.Create(new FlexiLedgerOptions()),
            TimeProvider.System);

        var stock = new Mock<IStockValueService>();
        stock
            .Setup(x => x.GetStockValueChangesAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MonthlyStockChange>());

        // Separate FinancialAnalysisService instances (own caches) share ONE LedgerService, so any
        // duplicate ledger query is attributable to LedgerService dedup, not to the analysis cache.
        FinancialAnalysisService CreateAnalysis() => new(
            ledger,
            stock.Object,
            NullLogger<FinancialAnalysisService>.Instance,
            Options.Create(new FinancialAnalysisOptions { MonthsToCache = 24 }),
            new MemoryCache(new MemoryCacheOptions()));

        await Task.WhenAll(
            CreateAnalysis().GetFinancialOverviewAsync(months: 3, includeStockData: false),
            CreateAnalysis().GetFinancialOverviewAsync(months: 3, includeStockData: false),
            CreateAnalysis().GetFinancialOverviewAsync(months: 3, includeStockData: false));

        calls.Should().NotBeEmpty();
        calls.Count.Should().BeLessThanOrEqualTo(calls.Distinct().Count());
    }
}
