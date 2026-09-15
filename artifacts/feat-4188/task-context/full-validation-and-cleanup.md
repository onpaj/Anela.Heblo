### task: full-validation-and-cleanup

**Files:** none new — this task runs the repository's standard validation gates against the two prior tasks' changes and fixes anything they surface.

- [ ] **Step 1: Run `dotnet format` to check/fix formatting**

Run: `dotnet format Anela.Heblo.sln --verify-no-changes`
Expected: no formatting violations. If violations are reported, run `dotnet format Anela.Heblo.sln`, review the diff is confined to the files touched by Tasks 1–2 (or pre-existing unrelated files — if so, do not commit those, per CLAUDE.md's "surgical changes" rule), and re-run `--verify-no-changes` to confirm.

- [ ] **Step 2: Full solution build**

Run: `dotnet build Anela.Heblo.sln`
Expected: Build succeeded, 0 warnings introduced, 0 errors.

- [ ] **Step 3: Run the full backend test suite for the touched project**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: PASS — all tests in the project, not just the Photobank subset, to catch any unexpected cross-feature regression (there should be none, since only Photobank files were touched).

- [ ] **Step 4: Self-review against the spec (FR-1, FR-2, FR-3, NFR-1, NFR-2)**

Manually confirm, by re-reading the final diff of `PhotobankIndexJob.cs` and `PhotobankPhotoTagRepository.cs`:
- FR-1: exactly one call to `GetPhotoTagsByPhotosAndSourceAsync` per batch — confirmed by the Step 5 test in Task 2.
- FR-2: exactly one call to `GetOccupiedTagPairsAsync` per batch, in-memory `HashSet` lookups replace `PhotoTagExistsAsync` — confirmed by the same test.
- FR-3 / architect's added within-batch duplicate-guard requirement: `addedPairsThisBatch` present and exercised by `UpsertPhotoBatch_DuplicateSharePointFileIdMatchingSameTagRule_AppliesTagOnceWithNoDuplicateKeyRisk`.
- NFR-1: no query count scales with batch size — confirmed by design (2 queries total, independent of `batchPhotoIds.Count`).
- NFR-2: no new external input — `batchPhotoIds` is derived entirely from `photosByFileId.Keys`/`tagNamesByPhoto.Keys`, already validated/owned by the batch; confirmed by reading the diff.
- No public signature of `UpsertPhotoBatchAsync` changed (still `private async Task UpsertPhotoBatchAsync(IReadOnlyList<GraphPhotoItem> batch, List<TagRule> tagRules, string? driveId, CancellationToken ct)`).

If any of these do not hold, fix inline and re-run Steps 2–3 before proceeding.

- [ ] **Step 5: Commit any formatting fixes from Step 1, if there were any**

```bash
git add -A
git commit -m "chore(photobank): apply dotnet format" || true
```

(The `|| true` is intentional — if `dotnet format` made no changes, there is nothing to commit and this step is a no-op.)

---

## Self-Review

**1. Spec coverage:**
- FR-1 (bulk-preload Rule-source tags) → Task 1 (adds the method) + Task 2 Step 3 (calls it once before the loop).
- FR-2 (bulk-preload occupied non-Rule pairs) → Task 2 Step 3 (`GetOccupiedTagPairsAsync` called once, `HashSet` lookups replace `PhotoTagExistsAsync`).
- FR-3 (preserve tag-reconciliation semantics, plus the architect-added within-batch duplicate guard) → Task 2 Step 3 (`addedPairsThisBatch`) + Step 5 regression test + the untouched `UpsertPhotoBatch_DuplicateSharePointFileIdMatchingSameTagRule_AppliesTagOnceWithNoDuplicateKeyRisk` test still passing.
- NFR-1 (O(1) queries per batch) → Task 2 Step 5 test asserts `Times.Once` for both bulk calls regardless of 3 distinct photos.
- NFR-2 (no new attack surface) → Task 3 Step 4 self-review checklist item.
- Data Model / API section (new `GetPhotoTagsByPhotosAndSourceAsync` signature, reused `GetOccupiedTagPairsAsync`) → Task 1.
- Out of Scope (no schedule/batch-size/`ReapplyRulesHandler` changes) → no task touches those; confirmed by the File Structure list above only naming the 4 files it names.

**2. Placeholder scan:** No "TBD"/"handle appropriately"/unshown code — every step above shows the exact code to write, exact commands, and exact expected output. No task says "similar to Task N" without repeating the code.

**3. Type consistency:** `PhotoTag`, `PhotoTagSource`, `int PhotoId`/`int TagId` (not `Guid`, per the actual repository code read during planning — the spec's illustrative `Guid`-based signature in its API/Interface Design section was superseded by the architect/design phases' correct `int`-based signature, which this plan follows) are used identically across Task 1's interface/implementation and Task 2's job code and test mocks. `GetPhotoTagsByPhotosAndSourceAsync` (plural, "Photos") is spelled identically everywhere it appears, distinct from the untouched singular `GetPhotoTagsByPhotoAndSourceAsync`.
