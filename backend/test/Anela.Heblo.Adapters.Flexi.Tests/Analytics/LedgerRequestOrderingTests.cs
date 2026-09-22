using FluentAssertions;
using Newtonsoft.Json;
using Rem.FlexiBeeSDK.Model.Accounting.Ledger;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Analytics;

/// <summary>
/// Pins the one SDK behaviour the resumable watermark in <c>LedgerSyncService.SyncAsync</c> rests
/// on: that the changed-since query is ordered by <c>lastUpdate</c>.
///
/// This exists because the claim was reversed once during review on the belief that the SDK sends
/// no `order` parameter. It does — `LedgerRequest(DateTime since)` sets `Order = "lastUpdate"`
/// right after the filter. If a future SDK ever drops that, keeping the high-water mark on a
/// failed run becomes unsound (an unordered page could push the watermark past rows that never
/// landed) and this test fails loudly instead of the sync losing rows quietly.
/// </summary>
public class LedgerRequestOrderingTests
{
    [Fact]
    public void ChangedSinceRequest_IsOrderedByLastUpdate_WhichIsWhatMakesTheWatermarkResumable()
    {
        var request = new LedgerRequest(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        request.Order.Should().Be("lastUpdate");
        request.Filter.Should().Contain("lastUpdate gte");
    }

    [Fact]
    public void ChangedSinceRequest_SerialisesTheOrderOntoTheWire()
    {
        // The property is only load-bearing if FlexiBee actually receives it.
        var json = JsonConvert.SerializeObject(
            new LedgerRequest(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        json.Should().Contain("\"order\":\"lastUpdate\"");
    }

    [Fact]
    public void DateRangeRequest_StillOrdersByAccountingDate_WhichIsWhatTheBackfillWindowsNeed()
    {
        var request = new LedgerRequest(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));

        request.Order.Should().Be("datUcto");
        request.Filter.Should().Contain("datUcto gte");
    }
}
