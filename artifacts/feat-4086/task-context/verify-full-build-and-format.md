### task: verify-full-build-and-format

**Files:** none modified — verification only, per CLAUDE.md's "Validation before completion" rule.

- [ ] **Step 1: Full backend build**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: build succeeds with 0 errors (warnings pre-existing elsewhere in the solution are not introduced by this change).

- [ ] **Step 2: Format check**

Run:
```bash
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```
Expected: no formatting violations reported for the four files touched in this plan. If it reports violations, run `dotnet format backend/Anela.Heblo.sln` (without `--verify-no-changes`) to apply fixes, then re-stage and amend/commit only the touched files.

- [ ] **Step 3: Run the full KnowledgeBase test suite (not just the one test class) to catch any missed reference**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~KnowledgeBase"
```
Expected: all KnowledgeBase-related tests pass, 0 failed.

- [ ] **Step 4: grep-confirm no `FileStream` reference remains anywhere in the KnowledgeBase module (production or test)**

Run:
```bash
grep -rn "FileStream" backend/src/Anela.Heblo.Application/Features/KnowledgeBase backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs backend/test/Anela.Heblo.Tests/KnowledgeBase
```
Expected: no output (no matches). (The three sibling modules — `CatalogDocuments/UploadMaterialDocument`, `CatalogDocuments/UploadPifDocument`, `Leaflet/UploadLeaflet` — are explicitly out of scope per the spec and will still show `FileStream`; do not touch them.)
