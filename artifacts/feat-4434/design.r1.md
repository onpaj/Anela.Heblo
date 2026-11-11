# Design: Unit tests for GetManufactureDifficultySettingsHandler

## Component Design
Single xUnit class `GetManufactureDifficultySettingsHandlerTests` with a constructor building the mocks, real mapper, fixed TimeProvider (now = 2026-06-15T00:00:00Z) and handler. Helper `Setting(int id, DateTime? from, DateTime? to)` builds a `ManufactureDifficultySetting` (ProductCode "P1", DifficultyValue 1, CreatedBy "test"); helper `SetupList(params settings)` configures `ListAsync`.

Test list (UTC dates):
1. Handle_NullFromAndTo_IsCurrentForAnyDate
2. Handle_NullValidFrom_CurrentWhenValidToNotBeforeReference (+ not current when ValidTo earlier) via Theory
3. Handle_NullValidTo_CurrentWhenValidFromNotAfterReference (+ not current when later) via Theory
4. Handle_TwoSettingsOnlyOneContainsDate_OnlyThatOneIsCurrent
5. Handle_OverlappingSettings_CurrentSettingIsFirstReturned (all matches flagged)
6. Handle_BoundaryDatesInclusive
7. Handle_AsOfDateSupplied_UsesAsOfDateNotNow (and ListAsync receives it)
8. Handle_NoAsOfDate_UsesTimeProviderNow
9. Handle_NoMatchingSetting_CurrentSettingNullAndAllNotCurrent
10. Handle_EmptyRepository_ReturnsEmptySettingsAndNullCurrent

## Data Schemas
No schema or API changes.
