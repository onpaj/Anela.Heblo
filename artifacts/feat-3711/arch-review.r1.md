# Architecture Review: Close test coverage gap in `PlaudCliClient`

## Skip Design: true

## Architectural Fit Assessment
This is a test-only addition to an existing xUnit test project (`Anela.Heblo.Adapters.Plaud.Tests`). No production code, interface, or contract changes are involved, and no UI/UX surface exists to change. The two target files already exist and already establish the exact conventions the new tests must follow:

- `PlaudCliClientParserTests.cs` — plain `[Fact]` tests calling `PlaudCliClient`'s public static parsers (`ParseFilesOutput`, `ParseFileDetail`), asserting with FluentAssertions. `ParseSummaryJson` is simply the third static parser on the same class that was never given a test class member — adding tests here is additive, not a new pattern.
- `PlaudCliClientRunTests.cs` — `[SkippableFact]` tests (via `Xunit.SkippableFact`) that skip on Windows (`Skip.If(OperatingSystem.IsWindows(), ...)`), spawn a bash shim script as the fake CLI executable, and use a hand-rolled `FakeRefreshClient : IPlaudTokenRefreshClient` (not a mocking library) to control the lower-level token-refresh HTTP call. The client under test is built through the *real* `PlaudTokenRefresher` wired to the fake HTTP client — this is important context: FR-4 asks for a *different* fake, `FakeTokenRefresher : IPlaudTokenRefresher`, which fakes one layer higher (the orchestration interface `PlaudCliClient` actually depends on), bypassing `PlaudTokenRefresher` entirely. Both fake styles already have precedent in this codebase (`FakeRefreshClient` in this file; compare `PlaudTokenRefreshClientTests.cs` for the sibling class's own fakes) — no new testing pattern is being introduced, just a new instance of the existing "hand-rolled fake implementing the interface directly" convention (explicitly called out in spec §Out of Scope: no mocking library).

There is nothing architecturally novel here. The job is mechanical: add `[Fact]`/`[Theory]` methods to one file and one `[SkippableFact]` method plus a nested fake class to another, following patterns already present line-for-line in the same files.

## Proposed Architecture

### Component Overview
No new components. Both target files already exist under:
```
backend/test/Anela.Heblo.Adapters.Plaud.Tests/
├── PlaudCliClientParserTests.cs   (add FR-1..FR-3 here)
├── PlaudCliClientRunTests.cs      (add FR-4; strengthen FR-5, FR-6 here)
├── PlaudAuthExceptionTests.cs     (unrelated — do not touch)
├── PlaudTokenRefreshClientTests.cs (unrelated — do not touch)
└── Fixtures/                       (no new fixture files needed; FR-1..FR-3 use inline JSON literals)
```
Production code under test (`PlaudCliClient.cs`, `IPlaudTokenRefresher.cs`, `PlaudAuthExpiredException.cs`) is read-only reference material — confirmed no changes needed for any FR.

### Key Design Decisions

#### Decision 1: One `[Theory]` vs. multiple `[Fact]`s for FR-1–FR-3
**Options considered:** A single `[Theory]` with `[InlineData]` covering all `ParseSummaryJson` shapes, vs. discrete `[Fact]` methods per named scenario.
**Chosen approach:** Discrete `[Fact]` methods, one per scenario, using the exact method names suggested in the spec (`ParseSummaryJson_WithValidJson_ExtractsHeadlineAndContent`, `ParseSummaryJson_WithMissingHeader_ReturnsEmptyHeadline`, etc.).
**Rationale:** Matches the existing style in `PlaudCliClientParserTests.cs`, where every scenario for `ParseFilesOutput`/`ParseFileDetail` is its own named `[Fact]` (e.g. `ParseFilesOutput_WithEmptyInput_ReturnsEmptyList`, `ParseFilesOutput_IgnoresLinesWithInvalidId`). A `[Theory]` would be locally more compact but would break the file's established one-scenario-one-method convention and produce less readable failure output (a named fact failing is self-explanatory; a theory failure requires reading `InlineData` to know which case broke). The malformed-JSON and empty-string cases (FR-3) are naturally two separate facts since the assertion for empty string is a degenerate but distinct case worth a dedicated name per the spec.

#### Decision 2: New `FakeTokenRefresher` for FR-4 instead of reusing `PlaudTokenRefresher` + `FakeRefreshClient`
**Options considered:** (a) Reuse the existing `CreateClient` helper (real `PlaudTokenRefresher` wired to `FakeRefreshClient`), writing a valid tokens file so the success path runs through the full stack; (b) hand-roll a new `FakeTokenRefresher : IPlaudTokenRefresher` that fakes `PlaudCliClient`'s direct dependency and counts calls itself.
**Chosen approach:** (b), a new `FakeTokenRefresher` nested class in `PlaudCliClientRunTests.cs`, per spec FR-4's explicit acceptance criteria.
**Rationale:** The unit under test for this coverage gap is `PlaudCliClient.RunCliAsync`'s interaction with `IPlaudTokenRefresher` (call it exactly once, ignore its result), not `PlaudTokenRefresher`'s own internals (already covered by `PlaudTokenRefreshClientTests.cs`). Faking one layer closer keeps the test's failure surface narrow: if it fails, the bug is in `PlaudCliClient`, not in token-refresh plumbing. Using option (a) would also require a real tokens file, real Key Vault wiring considerations (even if `secretClient: null`), and would not cleanly assert "`RefreshAsync == 0` calls" — with `FakeRefreshClient` that count is one layer removed from `PlaudCliClient`'s own retry decision. This mirrors the codebase's existing pattern of layering fakes at the seam nearest the code under test (see `IPlaudTokenRefreshClient` vs `IPlaudTokenRefresher` already being two distinct, separately-faked interfaces).

#### Decision 3: Strengthening FR-5/FR-6 in place vs. adding parallel new tests
**Options considered:** Add brand-new test methods asserting the stronger behavior alongside the existing `RunCli_WhenAuthFailsAndRefreshThrows_ThrowsPlaudAuthExpiredException` / `RunCli_WhenAuthFailsAndRetryAlsoFails_ThrowsPlaudAuthExpiredException`, vs. adding assertions directly into the existing test bodies.
**Chosen approach:** Strengthen in place (add assertions to the existing methods), per spec FR-5/FR-6.
**Rationale:** These are the same logical scenario with a deeper assertion, not a new scenario — duplicating the arrange/act blocks in a second method would violate the "surgical changes" principle and create two near-identical tests that could drift out of sync. FR-6 requires modifying the shim script body to append an invocation marker; this is a small, contained change to an existing test, not a new component.

## Implementation Guidance

### Directory / Module Structure
No new directories or files. All work happens in:
- `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudCliClientParserTests.cs` — add ~5-6 new `[Fact]` methods for FR-1–FR-3, appended after the existing `ParseFileDetail_IgnoresHeaderLines` method (keeps `ParseFilesOutput` → `ParseFileDetail` → `ParseSummaryJson` grouping order, mirroring declaration order in `PlaudCliClient.cs`).
- `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudCliClientRunTests.cs` — add a `FakeTokenRefresher` nested class (place it near `FakeRefreshClient`, under a `// ── Fake token refresher ──` comment banner matching the existing `// ── Fake refresh client ──` style), add the FR-4 `[SkippableFact]` method, and edit the two FR-5/FR-6 methods in place.

### Interfaces and Contracts
No production interfaces change. Test-only additions:

```csharp
// In PlaudCliClientRunTests.cs, alongside FakeRefreshClient:
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
This is constructed directly and passed into `new PlaudCliClient(NullLogger<PlaudCliClient>.Instance, Options.Create(OptionsFor(shimPath)), fakeRefresher)` — bypassing the existing `CreateClient` helper (which hardcodes the real `PlaudTokenRefresher`). FR-4's test needs its own inline client construction, not a change to the shared `CreateClient` helper (do not modify `CreateClient`'s signature — it's used by five other tests that all need the real `PlaudTokenRefresher` + `FakeRefreshClient` combination).

For FR-1–FR-3, no new types — tests call `PlaudCliClient.ParseSummaryJson(string json)` directly and assert against `PlaudSummaryResult.Headline` / `.MarkdownContent`.

### Data Flow
Both are pure call-and-assert flows, no new data flow to design:
- FR-1–FR-3: inline JSON string literal → `PlaudCliClient.ParseSummaryJson(json)` → assert `PlaudSummaryResult` fields.
- FR-4: bash shim (exit 0, valid `ParseFilesOutput`-compatible stdout) + `FakeTokenRefresher` → `client.ListRecentAsync(7)` → assert parsed result, `SyncCallCount == 1`, `RefreshCallCount == 0`.
- FR-5: bash shim (`AUTH_FAILED` on every call) + `FakeRefreshClient.Throws(theException)` where `theException` is captured in a local variable before construction → `client.ListRecentAsync(7)` → assert thrown `PlaudAuthExpiredException.InnerException` is reference-equal to `theException` and `.Message` contains `"token refresh failed"`.
- FR-6: bash shim modified to `echo invoked >> "$COUNTFILE"` before failing with `AUTH_FAILED` on every call → after `act` throws, read `COUNTFILE` and assert exactly 2 lines, alongside the existing `refreshClient.CallCount.Should().Be(1)`.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| FR-6's shim script writes to a counter file path that must be threaded through and cleaned up with the existing `try/finally Directory.Delete(dir, recursive: true)` block | Low | Put the counter file inside the existing per-test `dir` (from `CreateTestDir()`) so it's cleaned up automatically — no new cleanup logic needed. |
| FR-5's inner-exception reference-equality assertion is easy to get wrong if the exception is constructed inline inside `FakeRefreshClient.Throws(...)` instead of a named local | Low | Explicitly capture `var expectedInner = new HttpRequestException(...)` as a local *before* calling `FakeRefreshClient.Throws(expectedInner)`, then assert `ReferenceEquals(thrown.InnerException, expectedInner)` — this is spelled out in spec FR-5 and must not be skipped. |
| `[SkippableFact]` tests are skipped (not failed) on Windows; if CI coverage is measured only on a Windows runner, FR-4/FR-6 would not contribute to the 60% threshold | Medium | Verify (or ask) which OS the coverage-gate CI job runs on. Existing `RunCliAsync` tests already carry this same risk today, so this is a pre-existing condition, not new — but worth a quick confirmation before relying on FR-4/FR-6 to move the coverage number. If CI is Linux/macOS (typical for this kind of shim-script test suite), no action needed. |
| Test count growth in `PlaudCliClientRunTests.cs` makes the file harder to scan | Low | Keep the `// ── section banner ──` comment convention already used in the file so `FakeTokenRefresher` and the new FR-4 test are visually grouped and easy to find. |

## Specification Amendments
None. The spec is implementation-ready as written — method names, fake shapes, and assertion details are already fully specified (FR-1 through FR-6), and the "Out of Scope" section correctly rules out production code changes and new mocking libraries. No architectural gaps found during exploration; the spec's own investigation (documented in its Background section) already correctly identified that the two "existing" FR-5/FR-6 tests need strengthening rather than duplication, and that FR-4 covers a genuinely uncovered path (line 89's `SyncToKeyVaultAsync` call) rather than re-testing the AUTH_FAILED branches.

## Prerequisites
None. All target files, fixtures, and package references (`xunit`, `Xunit.SkippableFact`, `FluentAssertions`) already exist in `Anela.Heblo.Adapters.Plaud.Tests.csproj`. No migrations, config, or infrastructure changes required. Implementation can start immediately.
