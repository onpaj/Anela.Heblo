# Architecture Review: Unit tests for ComgateBankClient.GetStatementsAsync

## Skip Design: true

## Architectural Fit Assessment
Test-only change in the existing Bank test suite (`backend/test/Anela.Heblo.Tests/Features/Bank/ComgateBankClientTests.cs`). It follows the established pattern: `CreateClient(handler)` with `ResiliencePipeline.Empty` and a `FakeHttpHandler`. No production, DTO, API or process-doc changes (behaviour unchanged).

## Proposed Architecture

### Component Overview
Test -> ComgateBankClient -> HttpClient(FakeHttpHandler) ; pipeline = Empty (or a custom pipeline throwing BrokenCircuitException).

### Key Design Decisions
#### Decision 1: Fake handler, no MockHttp
Use existing `FakeHttpHandler`; extend (or add a small recording handler in the same test file) to capture request method + URI per call and return per-call responses. No new package.
#### Decision 2: Circuit-breaker test via custom ResiliencePipeline
Build a pipeline with `new ResiliencePipelineBuilder().AddCircuitBreaker(...)` pre-opened is brittle; instead use a handler-independent approach: construct client with a pipeline via `ResiliencePipelineBuilder` whose strategy throws `BrokenCircuitException` (e.g. `AddPipeline` of a callback strategy) or check how the existing GetStatementAsync circuit-breaker test does it and mirror it.
#### Decision 3: JSON bodies as literals
`ComgateStatementHeader` is internal; build response JSON strings (`application/json`) with the 5 properties.

## Implementation Guidance
### Directory / Module Structure
Edit only `ComgateBankClientTests.cs`; add tests named `GetStatementsAsync_<Condition>_<Expected>`.
### Interfaces and Contracts
Assert on `client.GetStatementsAsync(account, from, to)`; recorded requests: Method == POST, query contains `date=yyyy-MM-dd`.
### Data Flow
Loop day by day -> POST -> deserialize list -> filter by exact equality -> map to BankStatementHeader.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| `ReadAsAsync<T>` JSON casing mismatch with PascalCase literals | Low | Run test; adjust JSON casing to what formatter expects |
| FakeHttpHandler returns same response per call | Low | Recording handler with a per-call factory taking the request |
| Secret appears in logs | Low | Fake settings only |

## Specification Amendments
None.

## Prerequisites
None.
