### task: full-suite-validation

**Files:**
- None modified — validation only.

- [ ] **Step 1: Run the full backend test suite**

Run: `cd backend && dotnet test`
Expected: PASS — no regressions anywhere in the solution (this refactor touches only the Journal module, but the full suite must still be green per repo convention).

- [ ] **Step 2: Build**

Run: `cd backend && dotnet build`
Expected: Build succeeds with no new warnings/errors.

- [ ] **Step 3: Format check**

Run: `cd backend && dotnet format --verify-no-changes`
Expected: No formatting violations. If it reports changes, run `dotnet format` and re-verify, then amend the affected task's commit or add a small follow-up formatting commit.

- [ ] **Step 4: Commit (if formatting fixed anything)**

```bash
git add -A
git commit -m "chore(journal): apply dotnet format" --allow-empty
```

(Skip this commit if `dotnet format --verify-no-changes` in Step 3 reported no changes.)

---

## Self-Review

**1. Spec coverage:**
- FR-1 (single shared calculation, both handlers use it, byte-identical output, lives in Journal folder, no cross-module change) → covered by `add-pagination-calculator`, `refactor-get-journal-entries-handler`, `refactor-search-journal-entries-handler`.
- FR-2 (no contract changes) → verified implicitly: no task modifies `Contracts/GetJournalEntriesRequest.cs`, `Contracts/SearchJournalEntriesResponse.cs`, `JournalModule.cs`, or any controller; the handler tests assert the same public response properties as before.
- NFR-2 (single source of truth for the formula) → satisfied by construction: after `add-pagination-calculator`, the formula's only implementation is `JournalPaginationCalculator.Calculate`.

**2. Placeholder scan:** No TBD/TODO, no "similar to Task N", no unresolved types — every code block above is complete and self-contained (each handler file is shown in full, not as a diff snippet).

**3. Type consistency:** `JournalPaginationCalculator.Calculate(int, int, int) -> (int TotalPages, bool HasNextPage, bool HasPreviousPage)` is defined once in `add-pagination-calculator` and consumed identically (same parameter order, same tuple deconstruction names) in both handler refactor tasks.
