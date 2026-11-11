# Specification: Close verified coverage gaps in GoogleAdsNegativeKeywordHandler

## Summary
Issue #4518 reports 0% line coverage for `GoogleAdsNegativeKeywordHandler`. Investigation shows `backend/test/Anela.Heblo.Adapters.GoogleAds.Tests/Execution/GoogleAdsAddNegativeKeywordTests.cs` (plus the TestKit contract tests) already exercise the handler through `GoogleAdsActionExecutor`; a local run with coverlet gives 206 passing tests and ~95% line coverage for the handler class. The work is therefore to confirm the stale figure, add tests only for the branches still uncovered or only implicitly covered, and record the finding.

## Background
The handler is the sole path for adding/removing negative keywords on live Google Ads accounts. The issue's five concerns (TryParse, Normalize, 200-but-no-criterion guard, Revert prefix check, Campaign vs AdGroup routing) are mostly covered already. Remaining gaps (from the cobertura report: `RevertAsync` 92.9%) and thinner areas:
- Revert when original outcome is not Succeeded (Failed original with a resource id) and when original has null id on an action that is otherwise valid.
- Revert with an invalid action payload (TryParse error branch in RevertAsync).
- Revert/Execute with `validateOnly = true` at both levels.
- Ad-group level: prefix mismatch on revert, 200-without-criterion guard.
- TryParse: non-numeric target id, wrong target level (Ad, Account), missing payload keys, boundary values (exactly 80 chars OK, exactly 10 words OK), whitespace variants (tabs, newlines) collapse.
- ReadCurrentAsync: invalid action returns Missing without any API call; different text absent.

## Functional Requirements

### FR-1: Verify and document actual coverage
Run the GoogleAds test project with coverage and record the handler's line rate in the PR description. Note the CI figure of 0% appears to be a reporting artefact (the handler is `internal`, covered via the executor), not missing tests.
**Acceptance criteria:**
- Handler line coverage >= 90% reported locally by coverlet.

### FR-2: TryParse rejection tests
Add tests (via executor, ReadCurrent/Execute/Revert) for: level Ad and Account, non-numeric and empty target id, missing Text key, missing MatchType key, exactly 80 chars accepted, 81 rejected, 10 words accepted, 11 rejected, whitespace-only text rejected.
**Acceptance criteria:**
- Rejections yield Failed (Execute/Revert) or Exists=false (ReadCurrent), and zero API calls (no queries, no mutations).

### FR-3: Normalization tests
Tabs, newlines, repeated spaces collapse to single spaces before send; case-insensitive match on read.
**Acceptance criteria:** mutation body contains collapsed text; existing negative with different case/whitespace reads Present.

### FR-4: Revert guard tests
Failed original, Succeeded original with null id, invalid payload on revert, ad-group-level prefix mismatch, validateOnly revert.
**Acceptance criteria:** Failed outcome and `api.Mutations` empty (except validateOnly, which sends one request with `validateOnly: true`).

### FR-5: Level routing tests
Ad-group 200-without-criterion guard; validateOnly Execute at both levels uses the correct service/property.

## Non-Functional Requirements
### NFR-1: Performance
Tests are pure in-memory; whole project < 5 s.
### NFR-2: Security
No production code changes; no secrets; tests never hit the network.

## Data Model
None. Uses existing `StatefulFakeGoogleAdsApi`, `ExecutorHarness`.

## API / Interface Design
No production changes. Tests go in the existing `GoogleAdsAddNegativeKeywordTests.cs` (or a sibling `GoogleAdsNegativeKeywordHandlerTests.cs` in the same folder).

## Dependencies
xUnit 2.9.2, FluentAssertions 6.12; existing test support classes.

## Out of Scope
Production code changes (including the handler being `internal`), CI coverage-report configuration, GoogleAdsPauseAdHandler, live API smoke tests.

## Open Questions
None.

## Status: COMPLETE
