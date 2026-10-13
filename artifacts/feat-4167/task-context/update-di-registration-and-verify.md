### task: update-di-registration-and-verify

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs`

**Context:** This file registers `IDepartmentClient` → `FlexiDepartmentClient` in DI. Its `using Anela.Heblo.Domain.Features.InvoiceClassification;` (line 30 today) resolves the unqualified `IDepartmentClient` used in the registration call — it must move to `Anela.Heblo.Domain.Features.Analytics`. This is the file the original arch-review issue missed listing as a consumer; without this fix the solution will not compile. `DepartmentSyncService.cs` (registered a few lines below, at `services.AddScoped<IEntitySyncService, DepartmentSyncService>();`) is unrelated — it resolves its own `IDepartmentClient` type via a different `using Rem.FlexiBeeSDK.Client.Clients.Accounting.Departments;` already present in that file, not through this project's `using` list, so it needs no change here.

- [ ] **Step 1: View current relevant lines**

Run:
```bash
grep -n "using Anela.Heblo.Domain.Features.InvoiceClassification;\|AddScoped<IDepartmentClient" backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs
```
Expected:
```
30:using Anela.Heblo.Domain.Features.InvoiceClassification;
90:        services.AddScoped<IDepartmentClient, FlexiDepartmentClient>();
```

- [ ] **Step 2: Update the `using`**

Change line 30 from:
```csharp
using Anela.Heblo.Domain.Features.InvoiceClassification;
```
to:
```csharp
using Anela.Heblo.Domain.Features.Analytics;
```
Keep the line in the same alphabetically-grouped position among the other `using Anela.Heblo.Domain.Features.*;` lines (between `using Anela.Heblo.Domain.Features.Bank;` and `using Anela.Heblo.Domain.Features.Invoices;` alphabetically — `Analytics` sorts before `Bank`, so if the file's existing `using` block is not strictly alphabetized already, simply edit the line in place rather than reordering the block; do not reorder unrelated `using` lines).

Do not change line 90 (`services.AddScoped<IDepartmentClient, FlexiDepartmentClient>();`) or line 91 (`services.AddScoped<IDepartmentQueryService, FlexiDepartmentQueryService>();`) — both still compile correctly once the `using` resolves `IDepartmentClient` to `Anela.Heblo.Domain.Features.Analytics.IDepartmentClient`, matching `FlexiDepartmentClient`'s new base interface from the previous task. Do not touch line 116 (`services.AddScoped<IEntitySyncService, DepartmentSyncService>();`) — unrelated.

- [ ] **Step 3: Confirm no other reference to the old namespace remains anywhere in the backend source or tests**

Run:
```bash
cd backend
grep -rn "Anela\.Heblo\.Domain\.Features\.InvoiceClassification\.Department\b\|Anela\.Heblo\.Domain\.Features\.InvoiceClassification\.IDepartmentClient\b" src test
grep -rln "using Anela.Heblo.Domain.Features.InvoiceClassification;" src test | xargs -r grep -l "IDepartmentClient\|\bDepartment\b"
```
Expected: both commands produce no output. (The second command is a safety net: any file that still imports the old InvoiceClassification namespace *and* references `IDepartmentClient`/`Department` would indicate a missed consumer — there should be none.)

- [ ] **Step 4: Full solution build**

Run:
```bash
cd backend
dotnet build
```
Expected: `Build succeeded.` with 0 errors. (Per repo convention, also run `dotnet format` before considering the change complete — see Validation section of CLAUDE.md.)

- [ ] **Step 5: Run the affected test projects**

Run:
```bash
cd backend
dotnet test test/Anela.Heblo.Adapters.Flexi.Tests --filter "FullyQualifiedName~Departments"
dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~InvoiceClassification"
```
Expected: both commands report all tests passed, 0 failed. The first run covers `FlexiDepartmentQueryServiceTests` (updated) and `DepartmentSyncServiceTests` (untouched — confirms it still compiles and passes unmodified). The second confirms InvoiceClassification's own test suite is unaffected, since no InvoiceClassification file changed.

- [ ] **Step 6: Run the full backend test suite as a final safety net**

Run:
```bash
cd backend
dotnet test
```
Expected: all tests pass, 0 failed.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs
git commit -m "refactor(flexi): update DI registration using for relocated IDepartmentClient"
```

This is the final task — after this commit, `Anela.Heblo.Domain.Features.InvoiceClassification` no longer contains `Department.cs` or `IDepartmentClient.cs`, both live under `Anela.Heblo.Domain.Features.Analytics`, all four real consumers compile against the new namespace, `DepartmentSyncService.cs` is untouched, and the full backend solution builds and tests green.
