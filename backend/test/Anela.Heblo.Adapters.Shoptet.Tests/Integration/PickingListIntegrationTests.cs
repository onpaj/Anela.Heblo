using Anela.Heblo.Adapters.Shoptet.Tests.Integration.Infrastructure;
using Anela.Heblo.Application.Features.ExpeditionList;
using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Domain.Features.Logistics.Picking;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Integration;

[Collection("ShoptetIntegration")]
[Trait("Category", "Integration")]
public class PickingListIntegrationTests
{
    // Must match PrintPickingListRequest.DefaultSourceStateId (-2 = "Vyřizuje se").
    // Use statusId= query parameter (not status=) — the correct param name supports negative system IDs.
    private const int SourceStateId = PrintPickingListRequest.DefaultSourceStateId;

    private readonly IConfiguration _configuration;
    private readonly IEshopOrderClient _orderClient;
    private readonly IPickingListSource _source;
    private readonly IPrintQueueSink _sink;
    private readonly ITestOutputHelper _output;
    private readonly string _sinkFolder;

    public PickingListIntegrationTests(
        ShoptetIntegrationTestFixture fixture,
        ITestOutputHelper output)
    {
        _configuration = fixture.Configuration;
        _orderClient = fixture.ServiceProvider.GetRequiredService<IEshopOrderClient>();
        _source = fixture.ServiceProvider.GetRequiredService<IPickingListSource>();
        _sink = fixture.ServiceProvider.GetRequiredService<IPrintQueueSink>();
        _output = output;
        _sinkFolder = fixture.ServiceProvider.GetRequiredService<IOptions<PrintPickingListOptions>>().Value.PrintQueueFolder;
    }

    /// <summary>
    /// End-to-end test: fetches the 20 most recent orders from the store,
    /// temporarily moves them to "Vyřizuje se" (-2), runs the expedition list,
    /// verifies PDFs are produced, then restores all original states.
    ///
    /// Requires: Shoptet:IsTestEnvironment=true in user secrets.
    /// Excluded from CI via [Trait("Category", "Integration")] filter.
    /// Run manually in Rider or: dotnet test --filter "FullyQualifiedName~PickingListIntegrationTests"
    /// </summary>
    [Fact]
    public async Task PrintPickingList_ProducesPdfs_ForRecentOrders()
    {
        ShoptetTestGuard.Assert(_configuration);

        var ct = new CancellationTokenSource(TimeSpan.FromMinutes(5)).Token;

        // 1. Fetch 20 most recent orders and record their current states
        var orders = await _orderClient.GetRecentOrdersAsync(20, ct);
        if (orders.Count == 0)
            throw new InvalidOperationException(
                "No orders found in the test store. Cannot run picking list test.");

        _output.WriteLine($"FETCHED  {orders.Count} orders");
        // Only update orders not already in the target state — avoids unnecessary API calls
        // and means we only need to restore those we actually changed.
        var ordersToUpdate = orders.Where(o => o.StatusId != SourceStateId).ToList();
        var originalStates = ordersToUpdate.ToDictionary(o => o.Code, o => o.StatusId);

        _output.WriteLine($"SKIPPED  {orders.Count - ordersToUpdate.Count} orders already in state {SourceStateId}");

        var exportedFiles = new List<string>();
        try
        {
            // 2. Move only the orders that need it to "Vyřizuje se" (-2)
            foreach (var order in ordersToUpdate)
            {
                await _orderClient.UpdateStatusAsync(order.Code, SourceStateId, ct);
                _output.WriteLine($"SET      {order.Code} (was {order.StatusId}) → {SourceStateId}");
            }

            // 3. Run picking list — read-only, all carriers, ChangeOrderState = false
            var request = new PrintPickingListRequest
            {
                SourceStateId = SourceStateId,
                DesiredStateId = PrintPickingListRequest.DefaultDesiredStateId,
                Carriers = PrintPickingListRequest.DefaultCarriers,
                ChangeOrderState = false,
                SendToPrinter = false,
            };

            var result = await _source.CreatePickingList(request, async files =>
            {
                exportedFiles.AddRange(files);
                await _sink.SendAsync(files, ct);
                _output.WriteLine($"BATCH    {string.Join(", ", files.Select(Path.GetFileName))}");
            }, ct);

            _output.WriteLine($"RESULT   TotalCount={result.TotalCount}, PDFs={result.ExportedFiles.Count}");

            // 4. Assert — at least some orders had a recognized carrier shipping method
            result.TotalCount.Should().BeGreaterThan(0,
                "at least some of the 20 fetched orders should have a Zásilkovna/PPL/GLS/Osobak shipping method");
            result.ExportedFiles.Should().NotBeEmpty();
            foreach (var file in result.ExportedFiles)
                File.Exists(file).Should().BeTrue($"PDF {Path.GetFileName(file)} should exist on disk");
        }
        finally
        {
            // 5. Restore original states
            foreach (var (code, statusId) in originalStates)
            {
                try
                {
                    await _orderClient.UpdateStatusAsync(code, statusId, ct);
                    _output.WriteLine($"RESTORED {code} → {statusId}");
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"RESTORE FAILED {code}: {ex.Message}");
                }
            }

            // 6. Delete generated PDFs (temp files from source + sink output folder)
            foreach (var file in exportedFiles.Where(File.Exists))
                File.Delete(file);

            if (!string.IsNullOrEmpty(_sinkFolder) && Directory.Exists(_sinkFolder))
                Directory.Delete(_sinkFolder, recursive: true);
        }
    }

}
