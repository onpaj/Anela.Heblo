### task: verify-full-build-format-and-test-suite

**Files:**
- None modified — this task only runs verification commands against the changes made in the two prior tasks.

- [ ] **Step 1: Confirm `ApplicationModule.cs` has exactly one Logistics-related call**

Run: `grep -n "AddLogisticsModule\|AddGiftPackageManufactureModule\|AddGiftSettingsModule" backend/src/Anela.Heblo.Application/ApplicationModule.cs`
Expected output: exactly one line, `services.AddLogisticsModule();` — no `AddGiftPackageManufactureModule` or `AddGiftSettingsModule` matches.

- [ ] **Step 2: Confirm `LogisticsModule.cs` now calls both sub-modules**

Run: `grep -n "AddGiftPackageManufactureModule\|AddGiftSettingsModule" backend/src/Anela.Heblo.Application/Features/Logistics/LogisticsModule.cs`
Expected output: two lines — `services.AddGiftPackageManufactureModule();` and `services.AddGiftSettingsModule();`.

- [ ] **Step 3: Full solution build**

Run: `cd backend && dotnet build`
Expected: `Build succeeded.` with 0 errors. Warning count must not increase versus the pre-change baseline (capture the baseline warning count before Task 1/2's edits if not already known, and diff against it here).

- [ ] **Step 4: Format check**

Run: `cd backend && dotnet format --verify-no-changes`
Expected: exits 0 (no formatting violations). If it reports violations introduced by this change, run `dotnet format` (without `--verify-no-changes`) to fix them, then re-stage and amend the relevant commit from Task 1 or Task 2 (whichever file the formatter touched) — do not create a separate "fix formatting" commit for a change this small.

- [ ] **Step 5: Run the full backend test suite**

Run: `cd backend && dotnet test`
Expected: all tests pass, in particular any tests that build the full `IServiceCollection`/`IServiceProvider` via `AddApplicationServices()` (e.g. integration tests using `WebApplicationFactory`) — these must still resolve `IGiftPackageManufactureRepository`, `IGiftPackageManufactureService`, `IGiftSettingRepository`, `IValidator<SetGiftSettingCommand>`, and the `SetGiftSettingCommand` pipeline behavior without error, proving the DI graph is unchanged. No test currently pins the old call site (`grep -rn "AddGiftPackageManufactureModule\|AddGiftSettingsModule" backend/test/` returns no matches as of this plan), so no test file is expected to need updating.

- [ ] **Step 6: Final review — no leftover references**

Run: `grep -rn "AddGiftPackageManufactureModule\|AddGiftSettingsModule" backend/src/Anela.Heblo.Application/ApplicationModule.cs`
Expected: no matches (already confirmed in Step 1, re-checked here as the closing gate before declaring the task complete).

- [ ] **Step 7: Commit (only if Step 4 required a formatting fix that amended a prior commit; otherwise skip — Tasks 1 and 2 already committed everything)**

```bash
git status
```

Expected: clean working tree (nothing to commit) if Steps 3–6 all passed and no formatting fix was needed.

