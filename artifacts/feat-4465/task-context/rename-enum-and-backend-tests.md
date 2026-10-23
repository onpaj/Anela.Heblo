### task: rename-enum-and-backend-tests
**Files:** `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingFolderType.cs`; Marketing tests under `backend/test/Anela.Heblo.Tests/Domain/Marketing/` and `.../Application/Marketing/`; new `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingFolderTypeTests.cs`.

- [ ] Rename members: `General->Images`, `Seasonal->Texts`, `ProductLine->Videos`, `Campaign->Graphics`; keep ints 0,1,2,3 and `Other=99`.
- [ ] Replace old names in all backend test files (`grep -rnE "MarketingFolderType\.(General|Seasonal|ProductLine|Campaign)" backend` must return nothing).
- [ ] Add `MarketingFolderTypeTests` asserting `(int)` of each member (0,1,2,3,99).
- [ ] Run `dotnet build`, `dotnet format`, `dotnet test --filter Marketing`; all pass.
- [ ] Commit: `refactor(marketing): rename MarketingFolderType members to media categories`.

