# Architecture Review: OrgChartService success-path unit test

## Skip Design: true
Pure backend test-coverage addition. No UI, no API contract change, no new component — nothing for a designer to weigh in on.

## Architectural Fit Assessment
This is the smallest possible unit of work: one new `[Fact]` in an existing test class, no production code touched. It fits the existing test file perfectly — `OrgChartServiceTests.cs` already has the exact scaffolding a success-path test needs (`StubHttpMessageHandler.Returns(HttpStatusCode.OK, json)`, `CreateService(...)`, `VerifyNoErrorLog()`), all `private` and reusable as-is. There is no integration point beyond "add a method to this file." The spec's scope (one test, no production changes) is architecturally sound — I found nothing in `OrgChartService`, `OrgChartResponse`, or the DTOs that needs to change to make this testable; the gap is purely in test coverage, confirmed by reading `OrgChartService.cs` end to end.

One correction to ground the spec against the actual code: `docs/architecture/testing-strategy.md` lists AutoFixture as the project's test-data tool of choice, but `OrgChartServiceTests.cs` does not use it anywhere (all four existing tests hand-write their JSON/exception fixtures inline). The new test should match the file it lives in, not the general house style — see Decision 1.

## Proposed Architecture

### Component Overview
No components change. Test-only addition inside the existing test project:

```
backend/test/Anela.Heblo.Tests/Features/OrgChart/OrgChartServiceTests.cs
  └── [existing] GetOrganizationStructureAsync_WrapsHttpRequestException_...
  └── [existing] GetOrganizationStructureAsync_WrapsJsonException_...
  └── [existing] GetOrganizationStructureAsync_RethrowsGenericException_...
  └── [existing] GetOrganizationStructureAsync_ThrowsOnNullDeserialization_...
  └── [NEW]      GetOrganizationStructureAsync_ReturnsMappedResponse_WhenJsonIsValid
        ├── uses StubHttpMessageHandler.Returns(OK, validJson)   [existing helper]
        ├── uses CreateService(handler)                          [existing helper]
        └── uses VerifyNoErrorLog()                               [existing helper]
```

Production code under test, unchanged:
```
OrgChartService.GetOrganizationStructureAsync
  → HttpClient.GetAsync(_options.DataSourceUrl)
  → JsonSerializer.Deserialize<OrgChartResponse>(content, JsonOptions)
  → returns OrgChartResponse { Organization: OrganizationDto { Name, Positions: [PositionDto { ..., Employees: [EmployeeDto] }] } }
```

### Key Design Decisions

#### Decision 1: Inline JSON literal, not AutoFixture, and not the raw string-shell shown in the issue
**Options considered:**
- (a) Reuse the issue's suggested body verbatim (`{"organization":{"name":"Anela","positions":[]}}`) — matches only `Organization.Name`, `Positions` stays empty.
- (b) Use AutoFixture to generate an `OrgChartResponse`, then serialize it to JSON and feed that back through the stub.
- (c) Hand-write a richer inline JSON literal (raw string, matching the four existing tests' style) with one populated `PositionDto` and one nested `EmployeeDto`.

**Chosen approach:** (c).

**Rationale:** The issue's suggested body only proves `Organization.Name` maps and `Positions` deserializes to an empty list — it would not catch a rename inside `PositionDto` or `EmployeeDto`, which is exactly the class of regression FR-1 in the spec calls out (nested DTO field renames going undetected). AutoFixture (b) is the project's documented default for test data, but it's the wrong tool here: this test's whole point is to assert on *known, specific* field values coming out of a *known* JSON string — round-tripping through AutoFixture would generate the JSON from the C# type rather than independently asserting the JSON→C# mapping, weakening exactly the signal the test exists to provide. It would also be inconsistent with all four sibling tests in the same file, which hand-write their fixtures inline as C# raw string literals (`"""..."""`) using `StubHttpMessageHandler.Returns(...)`. Matching the file's own established pattern (Decision made once, by precedent, in this file) outweighs matching the general house style for a one-test addition.

#### Decision 2: Assert on nested `PositionDto`/`EmployeeDto` fields, not only `Organization.Name`
**Options considered:**
- (a) Assert only `Organization.Name` and that `Positions` is non-empty (matches the issue's literal suggested-fix snippet).
- (b) Assert `Organization.Name` plus specific fields on the first `PositionDto` (`Id`, `Title`, `Level`, `Department`) and its first `EmployeeDto` (`Name`, `IsPrimary`).

**Chosen approach:** (b), per spec FR-1's acceptance criteria.

**Rationale:** `BaseResponse`'s field-rename risk cited in the issue is real but shallow — `Success`/`ErrorCode` are simple properties on a base class already covered indirectly by every error-path test's `Success`/error assertions (this new test just needs to assert `Success == true` and `ErrorCode == null` to close that side). The deeper, previously-*un*covered risk is nested-DTO mapping: `PositionDto.Employees` and every property inside `PositionDto`/`EmployeeDto` are currently asserted by zero tests. A test that only checks `Organization.Name` (option a) reintroduces almost the same blind spot the issue is filed against, just one level up. Asserting concrete values on one position and one nested employee is the minimum that actually exercises the full deserialization tree while staying a single, focused `[Fact]`.

#### Decision 3: Keep `VerifyNoErrorLog()` in the new test
**Options considered:** include it, per spec FR-1 / for consistency with the four existing tests; or omit it since the success path obviously can't log an error.

**Chosen approach:** include it.

**Rationale:** All four existing tests call `VerifyNoErrorLog()` as their last assertion — it documents the "controller is the single owner of Error logging" invariant mentioned in each test's own comment. Keeping it in the new test is free (one line, existing helper) and keeps the five tests structurally uniform, which matters more than the marginal signal it adds on the success path.

## Implementation Guidance

### Directory / Module Structure
No new files, no new directories. The single change is inside:
`backend/test/Anela.Heblo.Tests/Features/OrgChart/OrgChartServiceTests.cs`

### Interfaces and Contracts
No interface or contract changes. The new test consumes only what already exists in this file and in the Application-layer contracts:
- `OrgChartService` (constructor + `GetOrganizationStructureAsync`) — unchanged.
- `OrgChartResponse : BaseResponse` — `Organization: OrganizationDto` (unchanged).
- `OrganizationDto` — `Name: string`, `Positions: List<PositionDto>` (unchanged).
- `PositionDto` — `Id, Title, Description, Level: int?, ParentPositionId, Department, Employees: List<EmployeeDto>, Url` (unchanged).
- `EmployeeDto` — `Id, Name, Email, StartDate, IsPrimary: bool, Url` (unchanged).
- Test-local: `StubHttpMessageHandler.Returns(HttpStatusCode, string)`, `CreateService(HttpMessageHandler)`, `VerifyNoErrorLog()` — all already `private`/exist in the class, reused as-is.

### Data Flow
1. New test builds a JSON raw string literal representing one organization, one position, one nested employee (all fields from Decision 2 populated with recognizable literal values, e.g. `"Anela"`, `"pos-1"`, `"CEO"`, department, level, and an employee name + `isPrimary: true`).
2. `StubHttpMessageHandler.Returns(HttpStatusCode.OK, json)` wraps it as the fake HTTP response body.
3. `CreateService(handler)` builds `OrgChartService` with that handler wired into its `HttpClient`.
4. Test calls `GetOrganizationStructureAsync(CancellationToken.None)` — production code runs unmodified: `GetAsync` → `EnsureSuccessStatusCode` → `ReadAsStringAsync` → `JsonSerializer.Deserialize<OrgChartResponse>`.
5. Test asserts the returned object's `Success`, `ErrorCode`, `Organization.Name`, and the populated `PositionDto`/`EmployeeDto` fields against the literals from step 1, then calls `VerifyNoErrorLog()`.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Test only re-asserts the happy path already implicitly proven by the app running in production, adding little real signal | Low | Mitigated by Decision 2 — asserting on nested DTO fields, not just the top-level shell, gives it a genuine regression-detection purpose (per the issue's own stated rationale) |
| A future change to `PositionDto`/`EmployeeDto` optional fields (e.g. `Url`, `ParentPositionId`) still wouldn't be caught since the test doesn't populate/assert every field | Low | Explicitly out of scope per spec — the issue asks for one test proving the mapping works, not exhaustive field coverage. Note for a future arch-review, not a blocker here |
| None identified around production code, since none is touched | N/A | N/A |

## Specification Amendments
One amendment to spec.r1.md's FR-1, based on grounding against the actual test file:
- The JSON fixture must be a hand-written inline literal (matching the four existing tests' style, using a C# raw string `"""..."""` the way `GetOrganizationStructureAsync_WrapsJsonException_...` does), **not** the issue's minimal `{"organization":{"name":"Anela","positions":[]}}` body verbatim — the spec's own acceptance criteria (asserting on nested `PositionDto`/`EmployeeDto` fields) already implies this, but it should be explicit: `Positions` must contain at least one entry with at least one nested employee, not stay empty as in the issue's snippet. AutoFixture is deliberately not used here (see Decision 1) despite being the project's general-purpose test-data tool.

No other amendments — the spec's scope, acceptance criteria, and out-of-scope boundary all hold up against the codebase as found.

## Prerequisites
None. No migrations, no config, no infrastructure — the test project, `xUnit`/`FluentAssertions`/`Moq` references, and all helper scaffolding already exist and build today.
