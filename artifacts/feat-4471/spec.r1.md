# Specification: OrgChart startup options validation wiring (#4471)

## Summary
Issue #4471 claims `OrgChartModule.AddOrgChartServices()` is never called, so `OrgChartOptions` validation is dead code. Investigation shows the claim is stale/incorrect: the call exists at `ApplicationModule.cs:112`, reached via `Program.cs:109` (`AddApplicationServices`). The remaining valuable work is a regression test proving the module is wired into the application composition root.

## Background
- `Program.cs:109` calls `AddApplicationServices(configuration, environment)`.
- `ApplicationModule.AddApplicationServices` calls `services.AddOrgChartServices(configuration)` (line 112).
- `AddOrgChartServices` binds `OrgChart` section, `ValidateDataAnnotations()`, `ValidateOnStart()`.
- `appsettings.json` provides `OrgChart:DataSourceUrl`.
- Existing tests call `AddOrgChartServices` in isolation only, so removal of the line in `ApplicationModule` would go undetected (the reporter's valid underlying concern).

## Functional Requirements

### FR-1: Do not add a duplicate registration
`Program.cs` must NOT receive a second `AddOrgChartServices` call (the issue's suggested fix); it is redundant.
**Acceptance criteria:**
- `Program.cs` unchanged; `AddOrgChartServices` referenced exactly once in production code outside its definition.

### FR-2: Wiring regression test
Add a test asserting that `AddApplicationServices` registers OrgChart options validation.
**Acceptance criteria:**
- Test fails if `services.AddOrgChartServices(configuration)` is removed from `ApplicationModule`.
- Test passes on current code.

## Non-Functional Requirements
### NFR-1: Performance
None.
### NFR-2: Security
No change.

## Data Model
None.

## API / Interface Design
None.

## Dependencies
`Microsoft.Extensions.Options`, existing test project `Anela.Heblo.Tests`.

## Out of Scope
Changes to other modules, Program.cs edits, option semantics.

## Open Questions
None.

## Status: COMPLETE
