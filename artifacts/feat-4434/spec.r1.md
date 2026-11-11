# Specification: Unit tests for GetManufactureDifficultySettingsHandler

## Summary
Add unit tests covering the "current setting" determination in `GetManufactureDifficultySettingsHandler` (currently 30% line coverage, 11/37). No production code changes; tests only.

## Background
The handler loads difficulty settings from `IManufactureDifficultyRepository.ListAsync`, maps them to `ManufactureDifficultySettingDto` via AutoMapper (IsCurrent ignored by the map), then computes `referenceDate = request.AsOfDate ?? _timeProvider.GetUtcNow()` and flags `IsCurrent = (ValidFrom == null || ValidFrom <= ref) && (ValidTo == null || ValidTo >= ref)`. The first matching DTO becomes `CurrentSetting`. Difficulty settings drive labour cost calculations, so a wrong flag or a silent fallback to "now" breaks historical views.

## Functional Requirements

### FR-1: Open-ended bounds
**Acceptance criteria:**
- A setting with null ValidFrom and null ValidTo is IsCurrent and is CurrentSetting for any reference date.
- A setting with only null ValidFrom is current when ValidTo >= reference; not current when ValidTo < reference.
- A setting with only null ValidTo is current when ValidFrom <= reference; not current when ValidFrom > reference.

### FR-2: Selection among several settings
**Acceptance criteria:**
- Two settings where only one range contains the reference date: that one is IsCurrent/CurrentSetting, the other is not.
- Two overlapping settings both containing the date: both get IsCurrent = true (as the code behaves), CurrentSetting is the first in repository order. (The brief says "the rest are not marked"; actual code marks all matches. Tests assert actual behaviour; see Open Questions.)
- Inclusive boundaries: ValidFrom == reference and ValidTo == reference are both current.

### FR-3: AsOfDate override
**Acceptance criteria:**
- With AsOfDate supplied, the reference date is AsOfDate, not the TimeProvider time (use a FakeTimeProvider/stub set to a different date such that the outcome differs).
- AsOfDate is passed through to `ListAsync`.
- Without AsOfDate, TimeProvider's UtcNow is used.

### FR-4: No current setting
**Acceptance criteria:**
- When no setting matches (or repository returns empty), CurrentSetting is null, all IsCurrent are false, Settings count matches, ProductCode echoed in response.

## Non-Functional Requirements
### NFR-1: Performance
Tests are pure in-memory; each runs in milliseconds.
### NFR-2: Security
Not applicable.

## Data Model
No changes. Uses `ManufactureDifficultySetting` (domain) and `ManufactureDifficultySettingDto`.

## API / Interface Design
No changes. Handler public contract is untouched.

## Dependencies
Moq, FluentAssertions, xUnit (already used in `backend/test/Anela.Heblo.Tests`), and a TimeProvider test double (`Microsoft.Extensions.Time.Testing.FakeTimeProvider` if the package is referenced, else a small `Mock<TimeProvider>` overriding `GetUtcNow`).

## Out of Scope
Changing handler behaviour, the mapping profile, controller/E2E tests, other handlers.

## Open Questions
None. Assumption: the brief's overlap case 3 is stated loosely; tests lock in current code behaviour (all matches flagged, first is CurrentSetting). If "only first flagged" is the desired product behaviour, that is a separate bug issue.

## Status: COMPLETE
