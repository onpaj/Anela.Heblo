### task: validate-and-verify-scope

Run the full validation gate from `CLAUDE.md` plus the two-file diff rule from the architecture review.

**Files:** none modified except by `dotnet format`, which should produce no diff.

- [ ] **Step 1: Build the whole solution**

Run: `dotnet build Anela.Heblo.sln`

Expected: `Build succeeded.` — 0 errors, and no new warnings attributable to the two changed files.

- [ ] **Step 2: Check formatting**

Run: `dotnet format backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --include backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs --verify-no-changes`

Expected: exit status 0, no `error WHITESPACE`/`error IDE…` output.

Run: `dotnet format backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --include backend/test/Anela.Heblo.Tests/Features/Logistics/Services/TransportBoxCompletionServiceTests.cs --verify-no-changes`

Expected: exit status 0, no output. If either command reports changes, run it again without `--verify-no-changes`, re-run the tests, and amend the last commit.

- [ ] **Step 3: Run the full backend test project**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`

Expected: `Passed!` with `Failed: 0`. This covers `TransportBoxCompletionServiceTests` (8 tests), `ApplicationStartupTests`, and `Architecture/ModuleBoundariesTests`.

- [ ] **Step 4: Verify the diff is exactly two files**

Run: `git diff --name-only origin/main...HEAD`

Expected: exactly these two lines and nothing else:

```
backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs
backend/test/Anela.Heblo.Tests/Features/Logistics/Services/TransportBoxCompletionServiceTests.cs
```

(`artifacts/feat-3888/*` files may appear if the pipeline committed them; any other **source** or **docs** path is a defect — revert it.)

- [ ] **Step 5: Confirm no unintended behavioural change**

Run: `git diff origin/main...HEAD -- backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs`

Expected: exactly four hunks — the field + constructor block, and the three call sites. Read the diff and confirm no log template, log level, error message, branch condition, `UpdateAsync`/`SaveChangesAsync` call, counter, or `BoxProcessingResult` value moved.

- [ ] **Step 6: Note the follow-up for the conflicting time-guidance docs**

No file change. When the PR description is written, record that `docs/architecture/DateTime_StandardizationGuide.md` §3 ("ALWAYS use `DateTime.UtcNow`") and `docs/architecture/Dev_Guidelines_time.md:14` (recommends `GetUtcNow().DateTime`) both contradict the convention this change follows, that both are repo-wide guidance deliberately left untouched here (Amendment #4), and that reconciling them belongs in a separate follow-up issue.

- [ ] **Step 7: Final commit if anything moved**

Only if Steps 2 or 5 required a fix:

```bash
git add backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs backend/test/Anela.Heblo.Tests/Features/Logistics/Services/TransportBoxCompletionServiceTests.cs
git commit -m "style(logistics): apply dotnet format to TransportBoxCompletion changes"
```

Otherwise no commit is needed — the working tree is already clean.

---

## Self-Review

**1. Spec coverage** (spec.r1.md FR-1 … FR-6, NFR-1 … NFR-4, as amended by arch-review.r1.md):

| Requirement | Covered by |
|---|---|
| FR-1 inject `TimeProvider`, last ctor param, `_timeProvider` field, no null guard, interface unchanged | `inject-timeprovider` Step 3 |
| FR-2 replace all three `DateTime.UtcNow` with `_timeProvider.GetUtcNow().UtcDateTime`, inline, no `SpecifyKind` | `use-injected-clock-for-transitions` Steps 5, 7 |
| FR-3 DI resolves with no registration change | `inject-timeprovider` Step 5 (`ApplicationStartupTests`); `validate-and-verify-scope` Step 4 (no `LogisticsModule.cs` in the diff) |
| FR-4 test class uses `FakeTimeProvider` frozen at `FrozenNow`, existing arrangement extended not restructured, all seven tests keep passing | `inject-timeprovider` Steps 1, 4; `clock-advance-regression-test` Step 4 (no real-clock reference) |
| FR-5 timestamp assertions on all three transition kinds + a clock-advance test + reintroduction fails a test | `use-injected-clock-for-transitions` Steps 1-3; `clock-advance-regression-test` Steps 1, 3 |
| FR-6 no behavioural change | `validate-and-verify-scope` Steps 4, 5 |
| NFR-1 performance / NFR-2 security | No action required; nothing in the plan alters call frequency, the `"System"` actor, or any secret surface |
| NFR-3 consistency (no `DateTime.UtcNow` under `.../Logistics/Services/`) | `use-injected-clock-for-transitions` Step 7 |
| NFR-4 determinism | Frozen `FakeTimeProvider` in `inject-timeprovider` Step 1; grep guard in `clock-advance-regression-test` Step 4 |
| Amendment #1 `StateDate` not `Date` | Every state-log assertion uses `stateLogEntry.StateDate` |
| Amendment #2 state-log assertions mandatory, no escape hatch | All four timestamp assertion blocks assert `StateLog` as well as `LastStateChanged` |
| Amendment #3 `.UtcDateTime` justified by in-memory equivalence | Reference facts section; grep guard in Step 7 |
| Amendment #4 conflicting docs untouched, follow-up noted | `validate-and-verify-scope` Steps 4, 6 |

No gaps.

**2. Placeholder scan:** every code step contains complete, compilable C#. No "TBD", no "add error handling", no "similar to task N" — the assertion block is repeated in full for each of the three transition tests rather than cross-referenced. Every command has an explicit expected result.

**3. Type consistency:** `_timeProvider` names the field in both the service (`TimeProvider`) and the test class (`FakeTimeProvider`) — distinct classes, distinct files, no collision. `FrozenNow` is `DateTimeOffset`; every assertion compares against `FrozenNow.UtcDateTime` (a `DateTime`), matching `LastStateChanged` (`DateTime?`) and `StateDate` (`DateTime`). `box.StateLog.Single()` returns `TransportBoxStateLog`, whose `State`, `StateDate`, `User`, `Description` members are all used exactly as declared. `_timeProvider.Advance(TimeSpan)` exists on `FakeTimeProvider`, which is why the field is typed `FakeTimeProvider` and not `TimeProvider`. The test constructor's fourth argument (`FakeTimeProvider`) binds to the service's fourth parameter (`TimeProvider`) by inheritance.
