# Architecture Review: Coverage for GoogleAdsNegativeKeywordHandler

## Skip Design: true
Backend test-only change; no UI.

## Architectural Fit Assessment
Fits the existing pattern: handlers in `Adapters.GoogleAds/Execution` are `internal` and exercised through `GoogleAdsActionExecutor` using `ExecutorHarness` + `StatefulFakeGoogleAdsApi` (an in-memory stateful fake of `IGoogleAdsApiClient`). Verified in code: `GoogleAdsAddNegativeKeywordTests` already has 20+ cases; local coverlet run: 206 tests pass, handler 95% line rate, `RevertAsync` state machine 92.9%. The issue text (0%, "mock IGoogleAdsApiClient") is stale or measures differently; no new mocking framework is needed and the fake is preferred over per-test mocks.

## Proposed Architecture

### Component Overview
Test -> ExecutorHarness.Create(api) -> GoogleAdsActionExecutor -> GoogleAdsNegativeKeywordHandler -> StatefulFakeGoogleAdsApi (records Queries and Mutations).

### Key Design Decisions
#### Decision 1: Test through the executor, not the handler
**Options:** InternalsVisibleTo + direct handler tests; through executor. **Chosen:** through executor. **Rationale:** matches existing tests, the guard (account check) and logging are part of the real path, and no production change is needed.
#### Decision 2: Extend existing test file
Add a new test class `GoogleAdsNegativeKeywordHandlerTests` beside the existing one only for new cases, to keep the existing file untouched (surgical changes).

## Implementation Guidance
### Directory / Module Structure
`backend/test/Anela.Heblo.Adapters.GoogleAds.Tests/Execution/GoogleAdsNegativeKeywordHandlerTests.cs` (new). Only add fake helpers if essential (e.g. nothing expected).
### Interfaces and Contracts
Use `ExecutorHarness.AddNegative(level, target, text, matchType)`; for non-numeric ids and missing payload keys construct `AdAction` directly (see harness for the constructor). `Create(api, validateOnly: true)` — note the executor forces validate-only outside Production; the harness default environment is "Production" so validateOnly takes effect. Validate-only results are `Failed` with error starting `ValidateOnly:`; check the request body carries `validateOnly: true`.
### Data Flow
Arrange fake (SeededApi: campaign 111, ad group 221) -> act -> assert outcome and `api.Mutations` / `api.Queries` counts.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Duplicating existing tests | Low | Read existing file first; only add listed gaps |
| Account guard rejects before handler (tests hit guard not handler) | Medium | Use `TestSettings.CustomerId` as harness does |
| Validate-only semantics misasserted | Low | Mirror `GoogleAdsValidateOnlyTests.cs` |

## Specification Amendments
FR-1 note: the 0% CI figure is not reproducible locally; report 95% and treat the issue as mostly resolved. Do not alter CI config.

## Prerequisites
None.
