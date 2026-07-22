# Design: Close test coverage gap in `PlaudCliClient`

## Component Design

Test-only change. No production types, interfaces, or contracts are added or modified. All work lands in two existing files in `Anela.Heblo.Adapters.Plaud.Tests`, following conventions already present in each.

### `PlaudCliClientParserTests.cs` — new `[Fact]` methods (FR-1–FR-3)
Six discrete `[Fact]` methods (no `[Theory]`, to match the file's existing one-scenario-one-method style used for `ParseFilesOutput`/`ParseFileDetail`), appended after `ParseFileDetail_IgnoresHeaderLines` so declaration order continues to mirror `PlaudCliClient.cs`'s parser order:

- `ParseSummaryJson_WithValidJson_ExtractsHeadlineAndContent`
- `ParseSummaryJson_WithMissingHeader_ReturnsEmptyHeadline`
- `ParseSummaryJson_WithHeaderButNoHeadline_ReturnsEmptyHeadline`
- `ParseSummaryJson_WithMissingAiContent_ReturnsEmptyContent`
- `ParseSummaryJson_WithMalformedJson_ReturnsEmptyHeadlineAndRawContent`
- `ParseSummaryJson_WithEmptyString_ReturnsEmptyHeadlineAndEmptyContent`

Each calls the existing public static `PlaudCliClient.ParseSummaryJson(string json)` directly and asserts against the returned `PlaudSummaryResult` via FluentAssertions. No fixtures or helpers needed — JSON inputs are inline string literals.

### `PlaudCliClientRunTests.cs` — new fake, one new test, two strengthened tests (FR-4–FR-6)

**New fake — `FakeTokenRefresher : IPlaudTokenRefresher`** (private nested class, placed near the existing `FakeRefreshClient`, under its own `// ── Fake token refresher ──` banner):

```csharp
private sealed class FakeTokenRefresher : IPlaudTokenRefresher
{
    public int RefreshCallCount { get; private set; }
    public int SyncCallCount { get; private set; }

    public Task RefreshAsync(CancellationToken ct = default)
    {
        RefreshCallCount++;
        throw new InvalidOperationException("should not be called");
    }

    public Task SyncToKeyVaultAsync(CancellationToken ct = default)
    {
        SyncCallCount++;
        return Task.CompletedTask;
    }
}
```

This fakes the seam `PlaudCliClient` depends on directly (`IPlaudTokenRefresher`), distinct from the existing `FakeRefreshClient : IPlaudTokenRefreshClient`, which fakes one layer lower (the HTTP refresh call used inside the real `PlaudTokenRefresher`). It is constructed and wired inline for the new test only — the shared `CreateClient` helper (used by five existing tests that need the real `PlaudTokenRefresher` + `FakeRefreshClient`) is not touched or overloaded.

**New test — `RunCli_WhenCliSucceeds_CallsSyncToKeyVaultAndReturnsOutput`** (`[SkippableFact]`, `Skip.If(OperatingSystem.IsWindows(), "Shim script requires bash")`):
- Arrange: bash shim exits 0 immediately, stdout is a valid `ParseFilesOutput`-compatible payload (e.g. `"Recordings in the last 7 days: 0"`); `PlaudCliClient` constructed directly with `new FakeTokenRefresher()`.
- Act: `client.ListRecentAsync(7)`.
- Assert: returned result matches the expected parsed output; `fake.SyncCallCount == 1`; `fake.RefreshCallCount == 0`.

**Strengthened — `RunCli_WhenAuthFailsAndRefreshThrows_ThrowsPlaudAuthExpiredException`** (FR-5): capture the `HttpRequestException` in a named local *before* passing it to `FakeRefreshClient.Throws(...)`; add assertions that the thrown `PlaudAuthExpiredException.InnerException` is reference-equal to that local, and that `.Message` contains `"token refresh failed"`. Existing `refreshClient.CallCount.Should().Be(1)` assertion is retained unchanged.

**Strengthened — `RunCli_WhenAuthFailsAndRetryAlsoFails_ThrowsPlaudAuthExpiredException`** (FR-6): shim script modified to `echo invoked >> "$COUNTFILE"` before failing with `AUTH_FAILED` on every invocation, with the counter file placed inside the test's existing `CreateTestDir()` temp dir (so the existing `try/finally Directory.Delete(dir, recursive: true)` cleans it up — no new cleanup logic). After `act` throws, assert the counter file contains exactly 2 lines (initial call + one retry, no runaway loop). Existing `refreshClient.CallCount.Should().Be(1)` assertion is retained unchanged.

## Data Schemas

No new or changed data schemas, DTOs, or API shapes — this task is test-only. Tests operate entirely on existing production types, unchanged:

- `PlaudSummaryResult` (`backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/PlaudSummaryResult.cs`) — `sealed record PlaudSummaryResult(string Headline, string MarkdownContent)`. Asserted fields only; no shape change.
- `IPlaudTokenRefresher` — `RefreshAsync(CancellationToken)`, `SyncToKeyVaultAsync(CancellationToken)`. The new `FakeTokenRefresher` implements this interface as-is.
- `PlaudAuthExpiredException` — the two-argument `(stderr, innerException)` constructor's behavior is asserted in FR-5; no change to the exception type itself.

Test fixtures are inline (JSON string literals for FR-1–FR-3; bash shim script bodies and local temp-dir counter files for FR-4/FR-6) — no new fixture files under `Fixtures/`.
