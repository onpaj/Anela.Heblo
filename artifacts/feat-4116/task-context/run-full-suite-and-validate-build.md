### task: run-full-suite-and-validate-build

**Context:** Final validation pass across everything touched by this feature, per this repo's standard (CLAUDE.md): `dotnet build` + `dotnet format` for backend changes, plus the full test suite.

**Files:** none (validation only).

- [ ] **Step 1: Run every test class touched or added by this feature**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GiftPackageManufactureServiceTests|FullyQualifiedName~GiftPackageManufactureAtomicityIntegrationTests|FullyQualifiedName~EmptyRepositoryExecuteInTransactionTests|FullyQualifiedName~MockPackingMaterialRepository|FullyQualifiedName~GetConsumptionHistoryQueryCountTests|FullyQualifiedName~PackingMaterialsListQueryCountTests"
```

Expected: all pass, `Failed: 0`.

- [ ] **Step 2: Run the full backend test suite**

```bash
cd backend && dotnet test
```

Expected: `Failed: 0` (the pre-existing suite plus every test added in this plan). Note the `GiftPackageManufactureAtomicityIntegrationTests` class requires Docker to be available (Testcontainers); if Docker is unavailable in the execution environment, this is the only class expected to be inconclusive — everything else must still pass.

- [ ] **Step 3: Build the whole solution**

```bash
cd /home/user/worktrees/feature-4116-Arch-Review-Logistics-Manufacture-And-Disassembly && dotnet build Anela.Heblo.sln
```

Expected: `0 Error(s)`.

- [ ] **Step 4: Format the code**

```bash
cd /home/user/worktrees/feature-4116-Arch-Review-Logistics-Manufacture-And-Disassembly && dotnet format
```

Expected: completes without error. If it rewrites any file this plan touched, re-run Step 2 and Step 3 to confirm the reformatted code still builds and passes.

- [ ] **Step 5: Verify formatting is clean**

```bash
cd /home/user/worktrees/feature-4116-Arch-Review-Logistics-Manufacture-And-Disassembly && dotnet format --verify-no-changes
```

Expected: exits successfully with no reported changes needed (if Step 4 already applied formatting).

- [ ] **Step 6: Final commit (only if `dotnet format` changed anything)**

```bash
git add -A
git commit -m "chore: apply dotnet format

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01LkwnHn1TLfFi9jP5okeVwP"
```

If `dotnet format` made no changes, skip this step — there is nothing to commit.
