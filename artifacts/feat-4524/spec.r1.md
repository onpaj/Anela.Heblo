# Specification: Unit tests for CostOptimizedTelemetryProcessor

## Summary
Add a unit test suite for `Anela.Heblo.API.Telemetry.CostOptimizedTelemetryProcessor` (currently 0% line coverage). The tests pin the request, dependency and trace filtering rules so that refactors cannot silently drop failed dependencies or flood Application Insights. No production code changes.

## Background
The processor intercepts all Application Insights telemetry. Rules: (1) requests - drop excluded path prefixes, static-file extensions, successful OPTIONS (ResponseCode "200" and Name contains "OPTIONS"), and fast (<10 ms) successful requests; (2) dependencies - pass failed ones straight through, drop successful SQL <50 ms and Http <100 ms; (3) traces - when `ASPNETCORE_ENVIRONMENT` is Production (or unset, defaulting to Production) drop Verbose and Information. Everything else is forwarded to `_next`. Sibling filters already have tests in `backend/test/Anela.Heblo.Tests/Infrastructure/Telemetry/` using Moq + FluentAssertions + xUnit.

## Functional Requirements

### FR-1: Request filtering tests
**Acceptance criteria:**
- Request to each representative excluded path (e.g. `/health`, `/api/health`, `/swagger/index.html`, `/favicon.ico`) is NOT forwarded; matching is case-insensitive prefix.
- Request for static extension (`/static/app.js`, `.css`, `.png`) is NOT forwarded.
- Successful OPTIONS request (ResponseCode "200", Name "OPTIONS /api/x") is NOT forwarded.
- Fast (5 ms) successful request is NOT forwarded; fast failed request (Success=false) IS forwarded; slow (>=10 ms) successful request IS forwarded.
- Request with null Url is forwarded when otherwise eligible.
- Documented current behaviour: a failed request to an excluded path (e.g. 500 on `/api/health`) is dropped (path check precedes success check); test pins this as characterization.

### FR-2: Dependency filtering tests
**Acceptance criteria:**
- Failed dependency of any type/duration (e.g. SQL 1 ms, Http 1 ms) IS forwarded.
- Successful SQL 30 ms NOT forwarded; successful SQL 60 ms forwarded; successful Http 50 ms NOT forwarded; successful Http 150 ms forwarded.
- Fast successful dependency of another type (e.g. "Azure blob") is forwarded.

### FR-3: Trace suppression tests
**Acceptance criteria:**
- Production: Verbose and Information NOT forwarded; Warning, Error, Critical forwarded.
- Staging and Development: all severities forwarded.
- Env var unset: treated as Production (Information dropped).
- Tests restore the original env var value after each test and are serialized (non-parallel xUnit collection) because the env var is process-global.

### FR-4: Passthrough
- Telemetry of other types (e.g. EventTelemetry) is forwarded.

## Non-Functional Requirements
### NFR-1: Performance
Tests are pure in-memory; whole class runs in well under 1 s.
### NFR-2: Security
None; no secrets or I/O.
### NFR-3: Maintainability
Follow style of existing `*TelemetryFilterTests.cs` (Moq `Mock<ITelemetryProcessor>`, FluentAssertions, namespace `Anela.Heblo.Tests.Infrastructure.Telemetry`).

## Data Model
None.

## API / Interface Design
Single new file: `backend/test/Anela.Heblo.Tests/Infrastructure/Telemetry/CostOptimizedTelemetryProcessorTests.cs`.

## Dependencies
Existing test project references (Moq, xunit, FluentAssertions, Application Insights via API project).

## Out of Scope
- Changing processor behaviour (including the health-path-failure drop).
- Process docs changes (no process behaviour change; test-only).
- E2E tests.

## Open Questions
None.

## Status: COMPLETE
