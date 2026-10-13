### task: final-verification

**Files:** None created or modified — this task only runs whole-solution verification and, if any leftover reference or formatting issue surfaces, fixes it inline before committing.

- [ ] **Step 1: Confirm no file outside the known list still imports the old namespace for the 8 moved types**

Run, from the repo root:

```bash
grep -rn "PackingMaterials\.Contracts" backend/src backend/test \
  | grep -E "GetPackingMaterialsList(Request|Response)|CreatePackingMaterial(Request|Response)|UpdatePackingMaterial(Request|Response)|UpdatePackingMaterialQuantity(Request|Response)"
```

Expected: **no output**. If anything prints, it is a missed `using`/reference to the old namespace for one of the 8 moved types — fix that file's using directive to point at the correct `UseCases.{Name}` namespace before proceeding (do not skip this — a lingering import to a deleted namespace/type is a build break, and a lingering import to a type that still happens to compile because of a wildcard-like coincidence would defeat the purpose of this refactor).

- [ ] **Step 2: Confirm the `Contracts/` folder no longer contains the four moved files**

Run:

```bash
ls backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/
```

Expected: contains only genuinely shared types — `ConsumptionDetailDto.cs`, `ConsumptionGroupBy.cs`, `ConsumptionGroupDto.cs`, `CreateAllocationRequestBody.cs`, `IInvoiceConsumptionSource.cs`, `InvoiceConsumptionHeader.cs`, `MaterialConsumptionHistoryItemDto.cs`, `PackingMaterialAllocationDto.cs`, `PackingMaterialDto.cs`, `PackingMaterialLogDto.cs`, `PackingMaterialsTextHelper.cs`, `UpdateAllocationRequestBody.cs`, `UpdateQuantityRequest.cs` — and none of `GetPackingMaterialsListRequest.cs`, `CreatePackingMaterialRequest.cs`, `UpdatePackingMaterialRequest.cs`, `UpdatePackingMaterialQuantityRequest.cs`, `UpdatePackingMaterialQuantityResponse.cs`.

- [ ] **Step 3: Full solution build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors, 0 new warnings compared to the pre-refactor baseline.

- [ ] **Step 4: Full solution test run**

Run: `dotnet test`
Expected: All tests PASS, including every test in `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/` (this exercises `MediatR`'s assembly-scan handler registration end-to-end for the four moved request/response pairs — confirming the architecture review's claim that namespace changes don't affect scan-based DI registration).

- [ ] **Step 5: Format check**

Run: `dotnet format --verify-no-changes`
Expected: No formatting violations. If it reports any (e.g. using-directive ordering), run `dotnet format` (without `--verify-no-changes`) to apply the fixes, review the diff to confirm it only touches formatting (no logic changes), then continue.

- [ ] **Step 6: Confirm the generated OpenAPI/TypeScript client is unaffected**

Run:

```bash
git status --porcelain frontend/src
```

first to confirm a clean baseline, then:

```bash
dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual
git status --porcelain frontend/src
git diff frontend/src
```

Expected: no diff. The four endpoints' routes, HTTP verbs, and request/response JSON shapes are unchanged, and the generator is driven by controller action signatures and DTO shapes, not by internal C# namespaces — per NFR-2 and the design doc's Data Schemas section. If a diff appears, treat it as a signal to investigate (do not commit it blindly) — it would mean some other assumption in this plan was wrong.

- [ ] **Step 7: Commit any formatting fixes from Step 5 (only if `dotnet format` changed anything)**

```bash
git add -u
git commit -m "chore(packing-materials): apply dotnet format after use-case relocation" --allow-empty-message -m "No-op if dotnet format made no changes"
```

If Step 5 reported no changes, skip this step — there is nothing to commit.

---

## Self-Review (performed while writing this plan)

**Spec coverage:**
- FR-1 (`GetPackingMaterialsList`) → `relocate-get-packing-materials-list`.
- FR-2 (`CreatePackingMaterial`) → `relocate-create-packing-material`.
- FR-3 (`UpdatePackingMaterial`) → `relocate-update-packing-material`.
- FR-4 (`UpdatePackingMaterialQuantity`) → `relocate-update-packing-material-quantity`.
- FR-5 (update every reference) → covered incrementally in each task's controller/test steps, plus verified exhaustively in `final-verification` Step 1 (a fresh grep, not a re-assertion of the plan's own predictions).
- NFR-1 (no behavior change) → every relocation task runs the pre-existing tests for that use case unmodified in assertions, only `using` edits.
- NFR-2 (build/format compliance, MediatR assembly-scan safety) → `final-verification` Steps 3–6.
- Architecture review's Specification Amendments (split combined files; controller using additions) → both applied (Steps 1–2 of `relocate-get-packing-materials-list` and `relocate-create-packing-material` split the combined files; every task's Step 5-equivalent adds the controller using).
- Out-of-scope items (`UpdateQuantityRequest`, other shared `Contracts` types, other modules, behavior/API changes, renames) → untouched by every task above; `UpdateQuantityRequest` is explicitly called out wherever it interacts with a `Contracts` using-removal decision (Step 6 of `relocate-update-packing-material`, Step 6 of `final-verification`'s Step 2 file list).

**Placeholder scan:** No "TBD"/"add error handling"/"similar to Task N" phrasing — every step shows the literal file content or the literal command to run.

**Type consistency:** Class names (`GetPackingMaterialsListRequest`/`Response`, `CreatePackingMaterialRequest`/`Response`, `UpdatePackingMaterialRequest`/`Response`, `UpdatePackingMaterialQuantityRequest`/`Response`), their members, and their namespaces are identical across every task that mentions them and match the actual current source read from the repository during planning (not reconstructed from memory) — including the one place the architecture review's own prediction (controller's `Contracts` using becoming fully unused) was checked against the real controller source and found incorrect, which this plan corrects rather than propagates.
