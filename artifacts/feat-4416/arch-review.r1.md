# Architecture Review: Regex timeout in InvoiceClassification rules

## Skip Design: true
Backend-only hardening, no UI.

## Architectural Fit Assessment
Pure Domain-layer change within three existing `IClassificationRule` implementations. Rules are stateless and instantiated by the rule evaluation engine; no DI, persistence or API impact. Existing tests live in `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/Rules/`.

## Proposed Architecture

### Component Overview
Three rule classes -> `Regex.IsMatch(input, pattern, IgnoreCase, timeout)`. Optional internal static helper `RegexMatcher` (or constant) in the `Rules` folder shared by all three.

### Key Design Decisions

#### Decision 1: Timeout value and location
**Options considered:** per-class constant; shared internal static class; configuration.
**Chosen approach:** a single `internal static class ClassificationRegex` in `Rules/` exposing `Timeout = 200 ms` and `bool IsMatchOrContains(string input, string pattern)` encapsulating the try/catch logic (timeout -> false, ArgumentException -> Contains).
**Rationale:** removes triplicated logic, one place to tune; configuration is overkill and Domain has no config access.

#### Decision 2: Timeout semantics
Timeout yields false (non-match), not Contains fallback, not exception. Catch `RegexMatchTimeoutException` before `ArgumentException` (it derives from TimeoutException, not ArgumentException, so order is not critical, but keep explicit).

#### Decision 3: No logging
Domain rules have no logger; skip. Noted as follow-up.

## Implementation Guidance

### Directory / Module Structure
- New: `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/Rules/ClassificationRegex.cs`
- Modify the three rule files to delegate to it.
- Tests: extend the three existing `*RuleTests.cs`.

### Interfaces and Contracts
`IClassificationRule` unchanged. Helper: `internal static bool IsMatchOrContains(string input, string pattern)`.

### Data Flow
Engine -> rule.Evaluate -> helper -> bool. Timeout surfaces as false.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| 200 ms too short for legit patterns on long descriptions | Low | Inputs are short strings; value is a single constant |
| Flaky timing test | Medium | Use `(a+)+$` with ~40 'a' + '!' input, assert false and elapsed generously < 5 s |
| Silent non-match hides bad rule | Low | Out of scope; follow-up logging/validation |

## Specification Amendments
None; FR-4 is resolved by the shared helper.

## Prerequisites
None.
