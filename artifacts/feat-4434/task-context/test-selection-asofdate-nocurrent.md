### task: test-selection-asofdate-nocurrent

**Files:** Modify the same test file.

- [ ] Add tests 4-6: only-one-matches, overlapping (all flagged, CurrentSetting is first), inclusive boundaries.
- [ ] Add tests 7-8: AsOfDate used instead of now (choose a setting valid only at the AsOfDate and not at now, and vice versa), `ListAsync` called with AsOfDate (`Verify`); no AsOfDate uses TimeProvider.
- [ ] Add tests 9-10: no match and empty repo -> `CurrentSetting` null, all `IsCurrent` false, ProductCode echoed.
- [ ] Run the filtered tests, then `dotnet build` and `dotnet format` on the solution; confirm handler coverage >60% if coverage tooling is available.
- [ ] Commit: `test(catalog): GetManufactureDifficultySettings selection, AsOfDate, no-current`.
