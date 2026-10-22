# Implementation: test-selection-asofdate-nocurrent (r1)

Extended `backend/test/Anela.Heblo.Tests/Features/Catalog/GetManufactureDifficultySettingsHandlerTests.cs` with 9 tests:
- only-one-matches, overlapping (all flagged, first is CurrentSetting), inclusive boundaries
- AsOfDate used instead of now (valid-at-asOf-only and valid-now-only), ListAsync called with AsOfDate, no AsOfDate uses TimeProvider (verified by advancing FakeTimeProvider)
- no match and empty repository -> CurrentSetting null, no IsCurrent, ProductCode echoed

`dotnet test --filter GetManufactureDifficultySettingsHandlerTests`: 16 passed. Test project build: 0 errors. `dotnet format --verify-no-changes` on the file: clean. No production code or process-doc impact.
