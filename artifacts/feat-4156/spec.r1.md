# Specification: OrgChartService success-path unit test

## Summary
`OrgChartServiceTests` currently exercises only the four error paths of `OrgChartService.GetOrganizationStructureAsync` (HTTP failure, JSON parse failure, generic exception, null deserialization) and has no test proving the happy path works. This spec adds exactly one new test that feeds a valid JSON body through the real deserialization path and asserts the resulting `OrgChartResponse` is mapped correctly, closing the regression gap called out in the arch-review finding.

## Background
`OrgChartService` (`backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/OrgChartService.cs`) deserializes the external HTTP JSON response directly into `OrgChartResponse` — the Application-layer contract DTO (`backend/src/Anela.Heblo.Application/Features/OrgChart/Contracts/OrgChartResponse.cs`), consistent with the direction set by issue #4154. Because `OrgChartResponse` derives from `BaseResponse` (`Success`, `ErrorCode`, `Params`) and additionally carries `Organization` (`OrganizationDto` → `Name`, `Positions: List<PositionDto>` → each with `Employees: List<EmployeeDto>`), there is real surface area for silent breakage: a field rename on `OrgChartResponse.Organization`, on `BaseResponse`'s constructor-set defaults, or on any nested DTO's property names would still let `JsonSerializer.Deserialize` succeed (returning a non-null object) while quietly dropping data — the existing error-path tests would not catch this because they only assert on thrown exceptions, never on a populated result. This is purely a test-coverage gap; no production code is suspected of being wrong today.

## Functional Requirements

### FR-1: Add a success-path unit test to `OrgChartServiceTests`
Add one `[Fact]` test method to `backend/test/Anela.Heblo.Tests/Features/OrgChart/OrgChartServiceTests.cs` that:
- Uses the existing `StubHttpMessageHandler.Returns(HttpStatusCode.OK, json)` helper (already present in the test class) to stub an HTTP 200 response whose body is a valid JSON document shaped like the external org-chart source.
- Calls `CreateService(...)` (the existing private helper) and invokes `GetOrganizationStructureAsync(CancellationToken.None)`.
- Asserts the returned `OrgChartResponse` is non-null, `Success` is `true`, `ErrorCode` is `null`, and `Organization.Name` matches the JSON input.
- Uses a JSON fixture rich enough to prove nested mapping, not just the top-level shell: at least one `PositionDto` with `Id`, `Title`, `Level`, `Department` populated, and at least one nested `EmployeeDto` with `Name` and `IsPrimary` populated — and asserts on those nested values too (not only `Organization.Name`), otherwise a rename inside `PositionDto`/`EmployeeDto` would still go undetected.
- Reuses `VerifyNoErrorLog()` (the existing private helper) to assert the success path does not log at `Error` level, for consistency with the four existing tests' logging assertion.

**Acceptance criteria:**
- Exactly one new `[Fact]` test method is added to `OrgChartServiceTests`; no existing test is modified or removed.
- The new test fails if `OrgChartResponse.Organization`, `BaseResponse.Success`, `BaseResponse.ErrorCode`, `PositionDto` (`Id`, `Title`, `Level`, `Department`, `Employees`), or `EmployeeDto` (`Name`, `IsPrimary`) is renamed or its JSON property mapping breaks, when run against the current `OrgChartService` implementation.
- The new test passes against the current, unmodified `OrgChartService.GetOrganizationStructureAsync` implementation (this is a coverage addition, not a bug-fix — no production code changes are expected).
- `dotnet build` and the full `Anela.Heblo.Tests` suite pass after the change.

## Non-Functional Requirements

### NFR-1: Performance
Not applicable — this is a single in-memory unit test with a stubbed `HttpMessageHandler`; no network, database, or timing constraints apply.

### NFR-2: Security
Not applicable — no new production code, no new external calls, no secrets or auth involved. The JSON fixture is inline test data.

## Data Model
No data model changes. Types referenced by the new test (all pre-existing, all in `backend/src/Anela.Heblo.Application/Features/OrgChart/Contracts/` unless noted):
- `OrgChartResponse : BaseResponse` (`backend/src/Anela.Heblo.Application/Shared/BaseResponse.cs` for the base) — adds `Organization: OrganizationDto`.
- `OrganizationDto` — `Name: string`, `Positions: List<PositionDto>`.
- `PositionDto` — `Id`, `Title`, `Description`, `Level: int?`, `ParentPositionId`, `Department`, `Employees: List<EmployeeDto>`, `Url`.
- `EmployeeDto` — `Id`, `Name`, `Email`, `StartDate`, `IsPrimary: bool`, `Url`.
- `BaseResponse` — `Success: bool` (defaults `true` via the parameterless constructor `OrgChartResponse()` calls), `ErrorCode: ErrorCodes?`, `Params: Dictionary<string,string>?`.

## API / Interface Design
No API or interface changes. This is a test-only change against the existing `IOrgChartService.GetOrganizationStructureAsync(CancellationToken)` contract and the existing `OrgChartService` implementation. No changes to `OrgChartController` or any HTTP-facing surface.

## Dependencies
- Existing test infrastructure only: `xUnit`, `FluentAssertions`, `Moq` (already referenced by `Anela.Heblo.Tests`), and the test class's own private `StubHttpMessageHandler` and `CreateService`/`VerifyNoErrorLog` helpers — no new packages.
- No dependency on issue #4154 being separately implemented; that issue is cited only as background for why `OrgChartResponse` is the deserialization target.

## Out of Scope
- Any change to `OrgChartService`, `OrgChartResponse`, `BaseResponse`, or any DTO's production code — this is a test-only addition.
- Additional edge-case tests beyond the one success-path test (e.g., multiple positions, empty `Positions` list, missing optional fields) — the suggested fix in the issue asks for one test; the spec follows that scope. Reviewers may flag more coverage as future work but it is not part of this unit.
- Changes to `OrgChartControllerTests` or `OrgChartModuleValidationTests` — out of scope, not mentioned in the finding.
- Refactoring the existing four error-path tests or the `StubHttpMessageHandler`/`CreateService` helpers.

## Open Questions
None.

## Status: COMPLETE
