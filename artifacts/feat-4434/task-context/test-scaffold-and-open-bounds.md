### task: test-scaffold-and-open-bounds

**Files:** Create `backend/test/Anela.Heblo.Tests/Features/Catalog/GetManufactureDifficultySettingsHandlerTests.cs`.

- [ ] Check whether the test csproj references `Microsoft.Extensions.TimeProvider.Testing`; if so use `FakeTimeProvider`, else add a private `FixedTimeProvider : TimeProvider` overriding `GetUtcNow()`.
- [ ] Build class scaffold per design.r1.md: `Mock<IManufactureDifficultyRepository>`, real `IMapper` from `CatalogMappingProfile` (fallback: mocked mapper), `Mock<ILogger<...>>`, fixed now = 2026-06-15 UTC, helpers `Setting(...)` and `SetupList(...)`.
- [ ] Write tests 1-3 (null/null; null ValidFrom Theory; null ValidTo Theory). Assert on `IsCurrent` and `CurrentSetting`.
- [ ] Run `dotnet test backend/test/Anela.Heblo.Tests --filter GetManufactureDifficultySettingsHandlerTests`; expect pass.
- [ ] Commit: `test(catalog): GetManufactureDifficultySettings open-bounded ranges`.

