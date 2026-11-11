# Architecture Review: OrgChart options wiring (#4471)

## Skip Design: true

## Architectural Fit Assessment
Project convention: feature modules register themselves from `ApplicationModule.AddApplicationServices`; `Program.cs` calls only that plus adapters. OrgChart already follows this (`ApplicationModule.cs:112`). The issue's premise (module never called) is incorrect; adding the call to `Program.cs` would break the convention and double-register options.

## Proposed Architecture

### Component Overview
Program.cs -> AddApplicationServices -> AddOrgChartServices -> OptionsBuilder<OrgChartOptions>.ValidateOnStart. Adapter: `AddOrgChartAdapter` (separate, unchanged).

### Key Design Decisions

#### Decision 1: No Program.cs change
**Options considered:** (a) add line per issue; (b) leave wiring, add regression test.
**Chosen approach:** (b).
**Rationale:** wiring already exists; (a) duplicates registration.

#### Decision 2: Test strategy
Build a ServiceCollection with in-memory configuration lacking `OrgChart:DataSourceUrl`, call `AddApplicationServices`, and assert that `IValidateOptions<OrgChartOptions>`/`IOptions<OrgChartOptions>` resolution throws `OptionsValidationException`. If `AddApplicationServices` needs too many other dependencies to build a provider, instead assert on descriptors: `services.Any(d => d.ServiceType == typeof(IValidateOptions<OrgChartOptions>))` (DataAnnotations validation registers this) and a `IConfigureOptions<OrgChartOptions>`. Descriptor inspection needs no provider build and is preferred.

## Implementation Guidance

### Directory / Module Structure
Extend `backend/test/Anela.Heblo.Tests/Features/OrgChart/OrgChartModuleValidationTests.cs` or add `OrgChartModuleWiringTests.cs` in the same folder.

### Interfaces and Contracts
`AddApplicationServices(IConfiguration, IHostEnvironment?)` requires a non-null `IHostEnvironment` (throws otherwise); use a stub (e.g. `Substitute`/Moq per existing test project convention) with `EnvironmentName = "Development"`.

### Data Flow
Test: empty config -> AddApplicationServices -> inspect descriptors for OrgChart options validation.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| AddApplicationServices throws on missing other config in test | Medium | Provide minimal config; fall back to descriptor-only check, or catch nothing and supply needed keys |
| Descriptor check is not OrgChart-specific | Low | Filter on generic arg `OrgChartOptions` |

## Specification Amendments
None.

## Prerequisites
None. Recommend a note on the issue that the premise was incorrect (done by the caller, not this stage).
