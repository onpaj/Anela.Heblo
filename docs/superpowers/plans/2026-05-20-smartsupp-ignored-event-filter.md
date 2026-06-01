# Smartsupp Webhook — Ignored Event Filter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a configurable drop-list to the Smartsupp webhook controller so matched events are silently discarded with `200 OK`, without creating audit rows, dispatching MediatR commands, or running HMAC verification.

**Architecture:** A new `IgnoredEventTypes` list on `SmartsuppOptions` is seeded via `appsettings.json`. On each incoming request, after the raw body is read and before the audit entry is constructed, the controller does a single lightweight JSON peek to extract only the `"event"` field and checks it against the list using ordinal comparison. On a match it logs a debug line and returns `200 OK` immediately — no audit, no HMAC check, no MediatR.

**Tech Stack:** .NET 8, ASP.NET Core, xUnit, FluentAssertions, `WebApplicationFactory` integration tests.

---

## File Map

| Action | File |
|--------|------|
| Modify | `backend/src/Adapters/Anela.Heblo.Adapters.Smartsupp/SmartsuppOptions.cs` |
| Modify | `backend/src/Anela.Heblo.API/appsettings.json` |
| Modify | `backend/src/Anela.Heblo.API/Controllers/SmartsuppWebhookController.cs` |
| Modify | `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppWebhookControllerTests.cs` |

---

### Task 1: Write the four failing tests + factory extension

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppWebhookControllerTests.cs`

- [ ] **Step 1: Add `SetIgnoredEventTypes` support to `SmartsuppWebhookFactory`**

`SmartsuppWebhookFactory` lives at the bottom of `SmartsuppWebhookControllerTests.cs` (after the test class). Add a field and method, and update `ConfigureTestWebHost` to write indexed in-memory config keys (the .NET binder reads `Smartsupp:IgnoredEventTypes:0`, `:1`, etc. into a `List<string>`).

Current `SmartsuppWebhookFactory` fields section (lines ~213–215):
```csharp
public class SmartsuppWebhookFactory : HebloWebApplicationFactory
{
    private string? _webhookAppId;
    private bool _replaceReactionsWithThrowing;
```

Replace with:
```csharp
public class SmartsuppWebhookFactory : HebloWebApplicationFactory
{
    private string? _webhookAppId;
    private bool _replaceReactionsWithThrowing;
    private List<string> _ignoredEventTypes = new();
```

Add after the existing `ReplaceReactionsWithThrowing()` method:
```csharp
    public void SetIgnoredEventTypes(IEnumerable<string> types) =>
        _ignoredEventTypes = types.ToList();
```

Replace the existing `ConfigureTestWebHost` body:
```csharp
    protected override void ConfigureTestWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var dict = new Dictionary<string, string?>
            {
                ["Smartsupp:WebhookSecret"] = "test-shared-secret",
                ["Smartsupp:WebhookAppId"] = _webhookAppId,
            };
            for (var i = 0; i < _ignoredEventTypes.Count; i++)
                dict[$"Smartsupp:IgnoredEventTypes:{i}"] = _ignoredEventTypes[i];
            config.AddInMemoryCollection(dict);
        });
    }
```

- [ ] **Step 2: Add the four new test methods to `SmartsuppWebhookControllerTests`**

Add after the existing `Receive_WritesAuditEntry_WhenHandlerThrows` test (around line 209):

```csharp
    [Fact]
    public async Task Receive_ReturnsOkWithNoAudit_WhenEventIsInIgnoreList()
    {
        using var factory = new SmartsuppWebhookFactory();
        factory.SetIgnoredEventTypes(["visitor.connected"]);
        var client = factory.CreateClient();
        var body = "{\"event\":\"visitor.connected\"}";

        var response = await client.SendAsync(BuildRequest(body, signature: null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var entries = await ReadAuditEntriesAsync(factory);
        entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Receive_ProcessesNormally_WhenEventIsNotInIgnoreList()
    {
        using var factory = new SmartsuppWebhookFactory();
        factory.SetIgnoredEventTypes(["visitor.connected"]);
        var client = factory.CreateClient();
        var body = """
            {
              "event": "conversation.exploded",
              "timestamp": "2026-05-20T10:00:00Z",
              "account_id": "acc-1",
              "app_id": "app-1",
              "data": {}
            }
            """;

        var response = await client.SendAsync(BuildRequest(body, Sign(body, Secret)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var entries = await ReadAuditEntriesAsync(factory);
        entries.Should().ContainSingle().Which.EventName.Should().Be("conversation.exploded");
    }

    [Fact]
    public async Task Receive_DoesNotFilter_WhenEventNameDiffersByCase()
    {
        using var factory = new SmartsuppWebhookFactory();
        factory.SetIgnoredEventTypes(["visitor.connected"]);
        var client = factory.CreateClient();
        var body = """
            {
              "event": "Visitor.Connected",
              "timestamp": "2026-05-20T10:00:00Z",
              "account_id": "acc-1",
              "app_id": "app-1",
              "data": {}
            }
            """;

        var response = await client.SendAsync(BuildRequest(body, Sign(body, Secret)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var entries = await ReadAuditEntriesAsync(factory);
        entries.Should().ContainSingle();
    }

    [Fact]
    public async Task Receive_AuditsMalformedJson_WhenIgnoreListConfigured()
    {
        using var factory = new SmartsuppWebhookFactory();
        factory.SetIgnoredEventTypes(["visitor.connected"]);
        var client = factory.CreateClient();
        var body = "not-json-at-all";

        var response = await client.SendAsync(BuildRequest(body, Sign(body, Secret)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var entries = await ReadAuditEntriesAsync(factory);
        entries.Should().ContainSingle()
            .Which.ProcessingStatus.Should().Be(SmartsuppWebhookProcessingStatus.MalformedJson);
    }
```

- [ ] **Step 3: Run the tests to confirm they fail (compile error or runtime failure)**

```bash
cd /path/to/repo
dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~Smartsupp" 2>&1 | tail -30
```

Expected: build error — `'SmartsuppOptions' does not contain a definition for 'IgnoredEventTypes'` — or if compilation succeeds but binding produces an empty list, the `BeEmpty()` assertion will fail. Either failure proves we're in RED state.

- [ ] **Step 4: Commit the failing tests**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppWebhookControllerTests.cs
git commit -m "test(smartsupp): add failing tests for ignored-event filter"
```

---

### Task 2: Implement `IgnoredEventTypes` on `SmartsuppOptions` + appsettings seed

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Smartsupp/SmartsuppOptions.cs`
- Modify: `backend/src/Anela.Heblo.API/appsettings.json`

- [ ] **Step 1: Add `IgnoredEventTypes` property to `SmartsuppOptions`**

Full file after change:
```csharp
namespace Anela.Heblo.Adapters.Smartsupp;

public class SmartsuppOptions
{
    public const string SectionKey = "Smartsupp";

    public string ApiToken { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.smartsupp.com/v2/";
    public int HttpTimeoutSeconds { get; set; } = 30;
    public string WebhookSecret { get; set; } = "";
    public string? WebhookAppId { get; set; }
    public List<string> IgnoredEventTypes { get; set; } = new();
}
```

- [ ] **Step 2: Seed `IgnoredEventTypes` in `appsettings.json`**

Locate the `"Smartsupp"` block (around line 531):
```json
  "Smartsupp": {
    "ApiToken": "-- stored in secrets.json --",
    "BaseUrl": "https://api.smartsupp.com/v2/",
    "WebhookSecret": "-- stored in secrets.json --",
    "WebhookAppId": ""
  },
```

Replace with:
```json
  "Smartsupp": {
    "ApiToken": "-- stored in secrets.json --",
    "BaseUrl": "https://api.smartsupp.com/v2/",
    "WebhookSecret": "-- stored in secrets.json --",
    "WebhookAppId": "",
    "IgnoredEventTypes": [ "visitor.connected" ]
  },
```

- [ ] **Step 3: Verify compilation**

```bash
dotnet build backend/ 2>&1 | tail -10
```

Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Smartsupp/SmartsuppOptions.cs \
        backend/src/Anela.Heblo.API/appsettings.json
git commit -m "feat(smartsupp): add IgnoredEventTypes to SmartsuppOptions and seed appsettings"
```

---

### Task 3: Add early-return filter + `TryPeekEventName` to controller

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/SmartsuppWebhookController.cs`

The relevant section of the `Receive` method currently reads (lines 61–67):

```csharp
        _metrics.RecordPayloadBytes(rawBody.Length);

        var headerValue = Request.Headers.TryGetValue(SignatureHeader, out var sig) ? sig.ToString() : null;
        var rawBodyText = Encoding.UTF8.GetString(rawBody);
        var headersJson = SerializeHeaders(Request);

        var entry = new SmartsuppWebhookAuditEntry
```

The filter must go **after** `var headersJson = SerializeHeaders(Request);` and **before** `var entry = new SmartsuppWebhookAuditEntry` so it runs before HMAC verification and before audit creation. `RecordPayloadBytes` intentionally fires before the filter (useful for monitoring dropped-traffic volume).

- [ ] **Step 1: Insert the early-return filter block**

After the line `var headersJson = SerializeHeaders(Request);`, insert:

```csharp
        if (TryPeekEventName(rawBody, out var ignoredEventName)
            && _options.IgnoredEventTypes.Contains(ignoredEventName, StringComparer.Ordinal))
        {
            _logger.LogDebug(
                "smartsupp webhook ignored event={Event} from {RemoteIp}",
                ignoredEventName, remoteIp);
            return Ok();
        }
```

- [ ] **Step 2: Add the `TryPeekEventName` static helper at the bottom of the class**

After the existing `TryGetUtc` method (the last private static in the class), add:

```csharp
    private static bool TryPeekEventName(byte[] rawBody, out string eventName)
    {
        eventName = "";
        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            if (doc.RootElement.TryGetProperty("event", out var v)
                && v.ValueKind == JsonValueKind.String)
            {
                eventName = v.GetString() ?? "";
                return eventName.Length > 0;
            }
        }
        catch (JsonException)
        {
            // Fall through — main flow handles malformed JSON.
        }
        return false;
    }
```

- [ ] **Step 3: Verify compilation**

```bash
dotnet build backend/ 2>&1 | tail -10
```

Expected: `Build succeeded.`

- [ ] **Step 4: Run all Smartsupp tests green**

```bash
dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~Smartsupp" 2>&1 | tail -20
```

Expected: all tests pass, including the 4 new ones.

Troubleshooting:
- `Receive_ReturnsOkWithNoAudit_WhenEventIsInIgnoreList` fails with 401 → the filter is placed after HMAC verification; move it before.
- `Receive_AuditsMalformedJson_WhenIgnoreListConfigured` returns empty entries → `TryPeekEventName` is incorrectly returning `true` for malformed JSON; verify the `catch (JsonException)` path returns `false`.

- [ ] **Step 5: Run dotnet format**

```bash
dotnet format backend/ 2>&1 | tail -5
```

Expected: no diff output.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/SmartsuppWebhookController.cs
git commit -m "feat(smartsupp): add configurable ignored-event drop-list to webhook controller"
```
