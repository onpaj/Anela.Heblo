### task: add-ga4-harness-conversions-helpers

**Files:**
- Modify: `backend/test/Anela.Heblo.Adapters.GoogleAnalytics.Tests/Ga4TestHarness.cs`

- [ ] **Step 1: Add `ConversionsSync` and `ConversionsRow` to `Ga4TestHarness`**

Open `backend/test/Anela.Heblo.Adapters.GoogleAnalytics.Tests/Ga4TestHarness.cs`. Immediately after the existing `TrafficRow` method (the one returning `new([date, channel], [sessions.ToString(), "0", "0", "0", "0", "0"])`), insert:

```csharp
    public static ConversionsSyncService ConversionsSync(
        Ga4DbContext dbContext, IGa4ReportClient client, Ga4SyncOptions options, DateTimeOffset now) =>
        new(client,
            new Ga4SyncWatermarkRepository(dbContext),
            dbContext,
            Microsoft.Extensions.Options.Options.Create(options),
            new FixedTimeProvider(now),
            NullLogger<ConversionsSyncService>.Instance);

    /// <summary>
    /// Row order matches ConversionsSyncService's Dimensions = ["date", "sessionDefaultChannelGroup"]
    /// and Metrics = ["transactions", "purchaseRevenue"] exactly — DimensionValues[0] is the date,
    /// DimensionValues[1] the channel group, MetricValues[0] transactions, MetricValues[1] revenue.
    /// </summary>
    public static Ga4Row ConversionsRow(string date, string channelGroup, long transactions, decimal purchaseRevenue) =>
        new([date, channelGroup], [transactions.ToString(), purchaseRevenue.ToString(CultureInfo.InvariantCulture)]);
```

No new `using` is required: `System.Globalization` (for `CultureInfo`), `Anela.Heblo.Adapters.GoogleAnalytics.Sync` (for `ConversionsSyncService`), and `Microsoft.Extensions.Logging.Abstractions` (for `NullLogger<T>`) are already imported at the top of this file.

- [ ] **Step 2: Build the test project to verify it compiles**

Run: `cd backend && dotnet build test/Anela.Heblo.Adapters.GoogleAnalytics.Tests/Anela.Heblo.Adapters.GoogleAnalytics.Tests.csproj`
Expected: `Build succeeded.` with no errors. (There is no test yet that calls these helpers, so this step only proves the new methods compile against the existing `ConversionsSyncService` constructor and `Ga4Row` record.)

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Adapters.GoogleAnalytics.Tests/Ga4TestHarness.cs
git commit -m "test(ga4): add ConversionsSync/ConversionsRow test harness helpers"
```

---
