### task: full-verification

**Files:** none (verification only)

- [ ] **Step 1: Full backend build**

Run: `cd backend && dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 2: Format check**

Run: `cd backend && dotnet format --verify-no-changes`
Expected: No formatting violations. If violations are reported, run `dotnet format` (without
`--verify-no-changes`) and commit the formatting fix separately.

- [ ] **Step 3: Full Logistics test run**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~Logistics"`
Expected: All tests pass, including the six new test files and both updated existing test
files.

- [ ] **Step 4: Full solution test run**

Run: `cd backend && dotnet test`
Expected: All tests pass (confirms no unrelated regression from the DI/constructor changes).

- [ ] **Step 5: Commit (only if step 2 produced formatting fixes not yet committed)**

```bash
git add -A
git commit -m "chore(logistics): apply dotnet format"
```

## Self-Review Notes

**Spec coverage:** FR-1 (per-transition side effects as isolated units) → tasks
`extract-*-side-effect`. FR-2 (handler keeps only orchestration) → task
`refactor-handler-orchestration`. FR-3 (extending transitions needs no handler edit) →
satisfied by the `IEnumerable<ITransportBoxTransitionSideEffect>` + DI registration mechanism
established across `create-side-effect-interface` and `register-di`. FR-4
(`RestoreInventoryForItemsAsync` placement) → task `extract-inventory-restorer` per arch-review
Decision 3. NFR-4 (existing tests keep passing unmodified in assertions) → task
`update-existing-tests`. The arch-review's dispatch-uniqueness risk → task
`add-dispatch-uniqueness-test`.

**Type consistency:** `ITransportBoxTransitionSideEffect.ExecuteAsync` and
`ITransportBoxInventoryRestorer.RestoreAsync` signatures are defined once in
`create-side-effect-interface` / `extract-inventory-restorer` and reused verbatim by every
later task (`refactor-handler-orchestration`, `update-existing-tests`,
`add-dispatch-uniqueness-test`) — no drift between them.

**Verification caveat:** exact signatures of `ITransportBoxRepository.GetPagedListAsync` and
`TransportBoxItem`'s constructor/`SourceInventoryId` mutability are referenced from what this
plan's author read in the current source; the implementing engineer must confirm these against
the live files (flagged inline at each usage) before treating a mismatch as a plan defect.
