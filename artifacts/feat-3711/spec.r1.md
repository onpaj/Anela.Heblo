# Specification: Close test coverage gap in `PlaudCliClient`

## Summary
`backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudCliClient.cs` sits at 3.8% line coverage (6/157 lines) against a 60% threshold. This work adds unit tests for the untested `ParseSummaryJson` parser and closes gaps in the `RunCliAsync` AUTH_FAILED retry path, including one scenario (the plain success path that calls `SyncToKeyVaultAsync`) that has no test coverage at all today. No production code changes are required or in scope — this is a test-only addition to `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudCliClientParserTests.cs` and `PlaudCliClientRunTests.cs`.

## Background
`PlaudCliClient` wraps the Plaud CLI binary to list recordings, fetch transcripts/summaries, and retry once on an expired-token (`AUTH_FAILED`) error. Two of its public/static surfaces are under-tested:

1. **`ParseSummaryJson`** (static) — parses the CLI's summary JSON output into a `PlaudSummaryResult`. It has zero dedicated tests in `PlaudCliClientParserTests.cs`, even though its sibling parsers (`ParseFilesOutput`, `ParseFileDetail`) are fully covered there.
2. **`RunCliAsync`** (private, exercised via `ListRecentAsync` et al.) — the AUTH_FAILED retry/refresh logic in `PlaudCliClientRunTests.cs` already has five `[SkippableFact]` tests covering: retry succeeds, refresh throws, retry-after-refresh-also-fails, tokens file missing, and non-AUTH_FAILED failure. Investigation while writing this spec found:
   - The two tests that most closely match the brief's items 4 and 5 (`RunCli_WhenAuthFailsAndRefreshThrows_ThrowsPlaudAuthExpiredException`, `RunCli_WhenAuthFailsAndRetryAlsoFails_ThrowsPlaudAuthExpiredException`) **already exist** and assert the exception *type*, but neither asserts the exact wrapping/no-extra-retry behavior the brief calls out (inner-exception identity for #4; that the CLI process is invoked exactly twice — no runaway retry — for #5).
   - **No existing test exercises the plain success path** (CLI succeeds on the first try, no AUTH_FAILED at all) — meaning line 89's `await _tokenRefresher.SyncToKeyVaultAsync(ct);` call and its "doesn't affect the return value" guarantee are entirely uncovered. This is the true gap behind the brief's item 3/6, not a missing test of the AUTH_FAILED branches.
   - Per `IPlaudTokenRefresher.SyncToKeyVaultAsync`'s XML doc, the method is contractually **"Best-effort — never throws."** `RunCliAsync` does not wrap the call in its own try/catch for generic exceptions — it relies entirely on that contract. The tests added here characterize actual behavior against that documented contract; they do not attempt to make `SyncToKeyVaultAsync` violate its own contract (see Out of Scope).

A regression in `ParseSummaryJson`'s fallback would cause meeting-task ingestion to silently return empty summaries. The AUTH_FAILED retry path is the only recovery mechanism for an expired Plaud token, so its exact wrapping/no-retry-loop behavior needs a direct assertion, not just a type check.

## Functional Requirements

### FR-1: `ParseSummaryJson` — valid JSON extraction
Add a test asserting that well-formed summary JSON with both `header.headline` and `ai_content` present is parsed into a `PlaudSummaryResult` whose `Headline` and `MarkdownContent` match the source values exactly.

**Acceptance criteria:**
- New test method (suggested name `ParseSummaryJson_WithValidJson_ExtractsHeadlineAndContent`) in `PlaudCliClientParserTests.cs`.
- Input: `{"header":{"headline":"Weekly Sync"},"ai_content":"# Notes\n- item one"}`.
- Asserts `result.Headline == "Weekly Sync"` and `result.MarkdownContent == "# Notes\n- item one"` via FluentAssertions.

### FR-2: `ParseSummaryJson` — missing-field fallbacks
Add tests covering every place `TryGetProperty` can miss, per the brief's callout of the `header.headline` path and the `ai_content` path:
1. `header` object absent entirely.
2. `header` object present but `headline` property absent.
3. `ai_content` property absent entirely.

**Acceptance criteria:**
- Three new test methods (or one `[Theory]` with `[InlineData]`/`[MemberData]` covering the three JSON shapes), e.g.:
  - `ParseSummaryJson_WithMissingHeader_ReturnsEmptyHeadline` — input `{"ai_content":"body text"}` → `Headline == string.Empty`, `MarkdownContent == "body text"`.
  - `ParseSummaryJson_WithHeaderButNoHeadline_ReturnsEmptyHeadline` — input `{"header":{},"ai_content":"body text"}` → `Headline == string.Empty`, `MarkdownContent == "body text"`.
  - `ParseSummaryJson_WithMissingAiContent_ReturnsEmptyContent` — input `{"header":{"headline":"Title Only"}}` → `Headline == "Title Only"`, `MarkdownContent == string.Empty`.
- Each asserts the field is `string.Empty` (not `null`), matching the `?? string.Empty` fallback in the source.

### FR-3: `ParseSummaryJson` — malformed JSON fallback
Add a test asserting the `JsonException` catch branch: when input is not valid JSON, the method returns `(string.Empty, rawJson)` — i.e. the *original, unparsed* input string is passed through as `MarkdownContent` verbatim, not an empty string or an error.

**Acceptance criteria:**
- New test method (suggested name `ParseSummaryJson_WithMalformedJson_ReturnsEmptyHeadlineAndRawContent`).
- Input: a non-JSON or truncated string, e.g. `"{ this is not valid json"`.
- Asserts `result.Headline == string.Empty` and `result.MarkdownContent` equals the exact input string (reference/value equality on the same literal used as input — proves passthrough, not reconstruction).
- Also covers the empty-string edge case (`ParseSummaryJson_WithEmptyString_ReturnsEmptyHeadlineAndEmptyContent`, input `""`) since `JsonDocument.Parse("")` throws `JsonException`, hitting the same catch branch with an edge-case input.

### FR-4: `RunCliAsync` — plain success path covers `SyncToKeyVaultAsync` without affecting the return value
Add a test for the previously-uncovered happy path: CLI succeeds on the first invocation (no `AUTH_FAILED`), confirming `SyncToKeyVaultAsync` is called exactly once after a successful run and that its outcome does not alter `ListRecentAsync`'s return value.

**Acceptance criteria:**
- New test method (suggested name `RunCli_WhenCliSucceeds_CallsSyncToKeyVaultAndReturnsOutput`) in `PlaudCliClientRunTests.cs`.
- Uses a new hand-rolled fake directly implementing `IPlaudTokenRefresher` (not the production `PlaudTokenRefresher` class) — e.g. `FakeTokenRefresher` with `RefreshCallCount` and `SyncCallCount` counters, `RefreshAsync` throwing `InvalidOperationException("should not be called")` if invoked, and `SyncToKeyVaultAsync` simply incrementing its counter and returning `Task.CompletedTask` (mirroring the interface's "never throws" contract — see Background).
- Shim script exits 0 immediately with a valid `ParseFilesOutput`-compatible payload (e.g. `"Recordings in the last 7 days: 0"`).
- Asserts: `result` matches the expected parsed output; `fake.SyncCallCount == 1`; `fake.RefreshCallCount == 0`.
- This closes the coverage gap on `PlaudCliClient.cs` line 89 (`await _tokenRefresher.SyncToKeyVaultAsync(ct);`) and line 90 (`return output;`) on the success path, which no existing test currently exercises.

### FR-5: `RunCliAsync` — refresh failure wraps the original exception
Strengthen the existing `RunCli_WhenAuthFailsAndRefreshThrows_ThrowsPlaudAuthExpiredException` test (or add a new one alongside it) to assert the *wrapping* behavior called out in the brief, not just the exception type.

**Acceptance criteria:**
- Assertion chain confirms:
  - The thrown `PlaudAuthExpiredException`'s `InnerException` is the exact `HttpRequestException` instance thrown by `FakeRefreshClient.Throws(...)` (use FluentAssertions' `.Where(ex => ReferenceEquals(ex.InnerException, theOriginalException))` or equivalent, capturing the original exception in a local variable before constructing the fake).
  - The outer exception's `Message` contains `"token refresh failed"` (the literal stderr string passed by `PlaudCliClient.RunCliAsync`'s catch block at the wrapping call site).
- `refreshClient.CallCount.Should().Be(1)` (already present) is retained.

### FR-6: `RunCliAsync` — retry after successful refresh does not loop
Strengthen the existing `RunCli_WhenAuthFailsAndRetryAlsoFails_ThrowsPlaudAuthExpiredException` test to assert the CLI process is invoked **exactly twice** (initial call + one retry) — proving there is no unbounded retry loop, not just that the final result is a thrown exception.

**Acceptance criteria:**
- Modify the shim script used in this test to append a marker line to a counter file (e.g. `echo invoked >> "$COUNTFILE"`) on every invocation, still always failing with `AUTH_FAILED`.
- After `act` throws, assert the counter file contains exactly 2 lines.
- Existing `refreshClient.CallCount.Should().Be(1)` assertion is retained (confirms `RefreshAsync` itself is also not retried).

## Non-Functional Requirements

### NFR-1: Performance
All new/modified tests must run in well under 1 second each (no live network calls, no real Plaud CLI, no `Task.Delay`/`Thread.Sleep`). `ParseSummaryJson` tests are pure in-memory static calls. `RunCliAsync` tests use local bash shim scripts and local temp files exactly as the existing `PlaudCliClientRunTests.cs` tests do.

### NFR-2: Security
No secrets, credentials, or real Plaud tokens are used. Fixture/fake tokens follow the existing pattern in `PlaudCliClientRunTests.cs` (e.g. `"old-token"`, `"refresh-token"`, far-future expiry). No test touches Key Vault — `SyncToKeyVaultAsync` is exercised only via the hand-rolled fake in FR-4, never the real `SecretClient`-backed `PlaudTokenRefresher`.

### NFR-3: Test isolation & platform
New `RunCliAsync`-level tests (FR-4, FR-6) must follow the existing `[SkippableFact]` + `Skip.If(OperatingSystem.IsWindows(), "Shim script requires bash")` pattern, since they spawn a bash shim script. `ParseSummaryJson` tests (FR-1–FR-3) are pure `[Fact]`s with no OS dependency, matching the existing parser tests in the same file.

### NFR-4: Coverage outcome
After this change, `PlaudCliClient.cs` line coverage must meet or exceed the 60% filter threshold referenced in the brief, with `ParseSummaryJson` (all branches: valid, header-missing, headline-missing, ai_content-missing, malformed/empty JSON) and the success-path branch of `RunCliAsync` (through `SyncToKeyVaultAsync` and `return output;`) fully exercised.

## Data Model
No new entities. Tests operate on the existing types:
- `PlaudSummaryResult` (`backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/PlaudSummaryResult.cs`) — `sealed record PlaudSummaryResult(string Headline, string MarkdownContent)`.
- `IPlaudTokenRefresher` (`RefreshAsync`, `SyncToKeyVaultAsync`) — new FR-4 fake implements this interface directly, distinct from the existing `FakeRefreshClient : IPlaudTokenRefreshClient` used by the other `RunCliAsync` tests (which fakes the lower-level HTTP refresh call, not the refresher orchestration).
- `PlaudAuthExpiredException` (`stderr` message ctor, `stderr, innerException` ctor) — FR-5 asserts against the two-argument ctor's behavior specifically.

## API / Interface Design
No production interfaces change. Test-only additions:
- `PlaudCliClientParserTests.cs`: up to 6 new `[Fact]` methods (or a `[Theory]`) calling the existing public static `PlaudCliClient.ParseSummaryJson(string json)`.
- `PlaudCliClientRunTests.cs`: 1 new `[SkippableFact]` method (FR-4) plus a new private `FakeTokenRefresher : IPlaudTokenRefresher` nested class; 2 existing `[SkippableFact]` methods strengthened in place (FR-5, FR-6) with additional assertions and, for FR-6, a modified shim script body.

## Dependencies
- `xunit` 2.9.2, `Xunit.SkippableFact` 1.5.61, `FluentAssertions` 6.12.0 — already referenced in `Anela.Heblo.Adapters.Plaud.Tests.csproj`; no new package references required.
- `System.Text.Json` — already used by the production code under test (`JsonDocument.Parse`, `JsonException`).
- Bash (`/bin/sh`) availability on the CI runner for the `[SkippableFact]` shim-script tests — same existing dependency as the current `RunCliAsync` tests; no new dependency introduced.

## Out of Scope
- Any change to `PlaudCliClient.cs`, `PlaudTokenRefresher.cs`, or `IPlaudTokenRefresher.cs` production code. This is a test-only task.
- Testing `SyncToKeyVaultAsync` violating its documented "never throws" contract. `RunCliAsync` intentionally does not defend against a contract violation there (only `PlaudAuthExpiredException` is caught around the success path); simulating a throw would test an unsupported/undefined scenario rather than real behavior. The real implementation's internal exception-swallowing (`PlaudTokenRefresher.SyncToKeyVaultAsync`'s own try/catch) is already covered separately by `PlaudTokenRefreshClientTests.cs`.
- Testing `RunCliCoreAsync`'s process-timeout (`TimeoutException`) or cancellation-token paths — not mentioned in the brief and not part of the identified `ParseSummaryJson`/AUTH_FAILED-retry gap.
- Raising or changing the 60% coverage filter threshold itself, or any CI/coverage-tooling configuration.
- Adding a mocking library (e.g. Moq, NSubstitute) — the project's established pattern is hand-rolled fakes implementing the relevant interface directly, and this task follows that pattern.

## Open Questions
None.

## Status: COMPLETE
