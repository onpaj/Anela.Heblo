### task: full-verification-and-cleanup

Full verification and cleanup

**Files:** none new — verification only.

- [ ] **Step 1: Full backend build**

Run: `cd backend && dotnet build`
Expected: 0 errors, 0 new warnings introduced by this change (pre-existing warnings elsewhere are out of scope).

- [ ] **Step 2: Format check**

Run: `cd backend && dotnet format --verify-no-changes`
Expected: no formatting diffs. If it reports diffs confined to the files touched in Tasks 1–4, run `dotnet format` (no `--verify-no-changes`) to apply them, then re-run `git add` on the affected files and amend the relevant task's commit is NOT done — instead make a new commit:
```bash
git add -A backend/src/Anela.Heblo.Application/Features/Manufacture backend/test/Anela.Heblo.Tests/Features/Manufacture
git commit -m "style(manufacture): apply dotnet format"
```

- [ ] **Step 3: Confirm no remaining production reference to `UpdateManufactureOrderDto` outside its own use case**

Run: `cd backend && grep -rln "UpdateManufactureOrderDto" src/ | grep -v "src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrder/"`
Expected: empty output — no production file outside the `UpdateManufactureOrder` use case folder references the DTO any more. (Test files may still reference `UpdateManufactureOrderRequest`/`UpdateManufactureOrderResponse`, which is fine and expected — this check is for `UpdateManufactureOrderDto`/`UpdateManufactureOrderSemiProductDto`/`UpdateManufactureOrderProductDto` specifically.)

- [ ] **Step 4: Run the full Manufacture test slice**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Manufacture"`
Expected: all tests PASS — this covers all four modified files plus any other Manufacture test that might incidentally reference these types (e.g. module-boundary or DI-wiring tests).

- [ ] **Step 5: Run the full backend test suite**

Run: `cd backend && dotnet test`
Expected: all tests PASS. This confirms no other module or wiring test (e.g. `ManufactureModule` DI registration tests) was affected by the constructor signature changes in Tasks 3–4.

- [ ] **Step 6: Manual acceptance check against the spec**

Confirm, by re-reading `ConfirmProductCompletionWorkflow.cs` and `ConfirmSemiProductManufactureWorkflow.cs` after all edits:
- Neither file references `UpdateManufactureOrderDto` anywhere in its method bodies (only, if at all, in a `using` retained for `UpdateManufactureOrderRequest`).
- Both constructors take `IManufactureOrderRepository`.
- Both `ExecuteAsync` methods call `_repository.GetOrderByIdAsync` exactly once, immediately after the `_mediator.Send(new UpdateManufactureOrderRequest {...})` call succeeds, and handle a `null` result without throwing.
- `IResidueDistributionCalculator.CalculateAsync` and `IManufactureNameBuilder.Build` take `ManufactureOrder`, not `UpdateManufactureOrderDto`.
- `UpdateManufactureOrderDto.cs`, `UpdateManufactureOrderResponse.cs`, `UpdateManufactureOrderHandler.cs` have zero diff against the pre-change version (`git diff origin/main -- backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrder/` should be empty).

- [ ] **Step 7: Final commit (if Step 6 required any fix-up)**

If Step 6 surfaced any leftover inconsistency, fix it and commit:
```bash
cd backend
git add -A src/Anela.Heblo.Application/Features/Manufacture test/Anela.Heblo.Tests/Features/Manufacture
git commit -m "fix(manufacture): final cleanup after ManufactureOrder decoupling review"
```
If nothing required fixing, this step is skipped — no empty commit.
