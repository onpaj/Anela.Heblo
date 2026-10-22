# Implementation Plan: GetManufactureDifficultySettingsHandler tests

Goal: raise coverage of `GetManufactureDifficultySettingsHandler` above 60% with unit tests; no production changes.
Validation: `dotnet build`, `dotnet format`, `dotnet test --filter GetManufactureDifficultySettingsHandlerTests`.

### task: test-scaffold-and-open-bounds

**Files:** Create `backend/test/Anela.Heblo.Tests/Features/Catalog/GetManufactureDifficultySettingsHandlerTests.cs`.

- [ ] Check whether the test csproj references `Microsoft.Extensions.TimeProvider.Testing`; if so use `FakeTimeProvider`, else add a private `FixedTimeProvider : TimeProvider` overriding `GetUtcNow()`.
- [ ] Build class scaffold per design.r1.md: `Mock<IManufactureDifficultyRepository>`, real `IMapper` from `CatalogMappingProfile` (fallback: mocked mapper), `Mock<ILogger<...>>`, fixed now = 2026-06-15 UTC, helpers `Setting(...)` and `SetupList(...)`.
- [ ] Write tests 1-3 (null/null; null ValidFrom Theory; null ValidTo Theory). Assert on `IsCurrent` and `CurrentSetting`.
- [ ] Run `dotnet test backend/test/Anela.Heblo.Tests --filter GetManufactureDifficultySettingsHandlerTests`; expect pass.
- [ ] Commit: `test(catalog): GetManufactureDifficultySettings open-bounded ranges`.

### task: test-selection-asofdate-nocurrent

**Files:** Modify the same test file.

- [ ] Add tests 4-6: only-one-matches, overlapping (all flagged, CurrentSetting is first), inclusive boundaries.
- [ ] Add tests 7-8: AsOfDate used instead of now (choose a setting valid only at the AsOfDate and not at now, and vice versa), `ListAsync` called with AsOfDate (`Verify`); no AsOfDate uses TimeProvider.
- [ ] Add tests 9-10: no match and empty repo -> `CurrentSetting` null, all `IsCurrent` false, ProductCode echoed.
- [ ] Run the filtered tests, then `dotnet build` and `dotnet format` on the solution; confirm handler coverage >60% if coverage tooling is available.
- [ ] Commit: `test(catalog): GetManufactureDifficultySettings selection, AsOfDate, no-current`.
