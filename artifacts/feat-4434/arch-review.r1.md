# Architecture Review: Unit tests for GetManufactureDifficultySettingsHandler

## Skip Design: true
Backend test-only change, no UI.

## Architectural Fit Assessment
Pure unit tests in the existing Catalog test folder, following `DeleteManufactureDifficultyHandlerTests` / `UpdateManufactureDifficultyHandlerTests` (Moq for repository/logger, FluentAssertions, xUnit). No production changes; no module boundary impact. No process doc update needed (no behaviour change).

## Proposed Architecture

### Component Overview
`GetManufactureDifficultySettingsHandlerTests` -> handler under test, with `Mock<IManufactureDifficultyRepository>`, a real AutoMapper `IMapper` (built from `CatalogMappingProfile`) , `Mock<ILogger<>>`, and a fixed-time `TimeProvider`.

### Key Design Decisions

#### Decision 1: Real AutoMapper vs mocked IMapper
**Options considered:** mock IMapper (as in Update tests, requires manual emulation) vs real mapper from `CatalogMappingProfile`.
**Chosen approach:** real mapper via `new MapperConfiguration(c => c.AddProfile<CatalogMappingProfile>()).CreateMapper()`.
**Rationale:** the handler maps every record; a real mapper keeps tests short and also verifies ValidFrom/ValidTo flow into the DTO. Verify the profile constructs without extra dependencies (it has plain CreateMap calls); if the constructor needs args, fall back to a mock mapper with a `Map` setup.

#### Decision 2: TimeProvider
**Chosen approach:** use `FakeTimeProvider` (Microsoft.Extensions.TimeProvider.Testing) if the test csproj already references it; otherwise a tiny private `FixedTimeProvider : TimeProvider` subclass overriding `GetUtcNow()`. Do not add a new package solely for this.

## Implementation Guidance

### Directory / Module Structure
Create `backend/test/Anela.Heblo.Tests/Features/Catalog/GetManufactureDifficultySettingsHandlerTests.cs`, namespace `Anela.Heblo.Tests.Features.Catalog`.

### Interfaces and Contracts
Stub `ListAsync(productCode, asOfDate, ct)` with `It.IsAny` matchers or exact args where passthrough is verified. Note the repository mock ignores date filtering, so tests control the returned list directly.

### Data Flow
request -> repo.ListAsync -> map -> compute referenceDate -> flag IsCurrent -> response.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Brief's overlap expectation differs from code (all matches flagged) | Low | Assert actual behaviour; documented in spec |
| `referenceDate` is DateTimeOffset compared with DateTime (implicit conversion, Kind-sensitive) | Low | Use `DateTimeKind.Utc` dates in tests |
| Test-project lacks FakeTimeProvider | Low | Fixed TimeProvider subclass |

## Specification Amendments
None.

## Prerequisites
None.
