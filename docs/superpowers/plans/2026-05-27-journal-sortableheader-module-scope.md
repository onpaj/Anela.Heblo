# Journal `SortableHeader` Module-Scope Refactor — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the `SortableHeader` helper out of the `JournalList` function body to module scope in the same file, so React stops unmounting/remounting the table header subtree on every state change.

**Architecture:** Single-file structural refactor. Declare `SortableHeader` (and a named `SortableHeaderProps` interface) at module scope alongside the existing `JournalRow` helper. Replace closure capture of `sortBy`, `sortDescending`, and `handleSort` with explicit props threaded from `JournalList`. No behavior change, no new packages, no API contract changes, no shared component extraction.

**Tech Stack:** TypeScript 5.x, React 18, Jest + React Testing Library (existing test setup), `lucide-react` icons (already imported).

---

## File Structure

Only one source file is touched:

- **Modify:** `frontend/src/components/pages/Journal/JournalList.tsx`
  - Insert `SortableHeaderProps` interface + `SortableHeader` component **between** the existing module-scope `JournalRow` (ends line 116) and the `JournalList` declaration (starts line 118).
  - Delete the nested `SortableHeader` declaration (lines 246–274) and its leading `// Sortable header component` comment.
  - Update the three `<SortableHeader>` call sites inside `JournalList`'s render (lines 384–388) to pass `sortBy`, `sortDescending`, `onSort`.

The test file is consulted (to confirm baseline GREEN) and optionally extended with one smoke test:

- **Consult / optionally extend:** `frontend/src/components/pages/Journal/__tests__/JournalList.test.tsx`

No new files. No documentation files. No memory/notes files.

---

## Task 1: Establish a clean baseline

Before changing anything, prove the working tree builds, lints, and tests pass. This is non-negotiable — if the baseline is red, you'll waste hours blaming the refactor for a pre-existing failure.

**Files:** none modified.

- [ ] **Step 1: Confirm working tree is clean**

Run from repo root:
```bash
git status
```
Expected: `nothing to commit, working tree clean` (or only this plan file as a new/modified path).

- [ ] **Step 2: Run frontend build**

Run from repo root:
```bash
cd frontend && npm run build
```
Expected: exit code 0, no TypeScript errors, no new warnings. If it fails, **stop**, report the failure, and do not proceed.

- [ ] **Step 3: Run frontend lint**

Run from `frontend/`:
```bash
npm run lint
```
Expected: exit code 0, no errors. Existing warnings (if any) are tolerated but should be noted; the refactor must not add new ones.

- [ ] **Step 4: Run the existing JournalList tests**

Run from `frontend/`:
```bash
npm test -- --watchAll=false JournalList
```
Expected: all tests pass (arch-review counts 17 in `JournalList.test.tsx`; the exact count is informational — the requirement is "all green"). If any baseline test is red, **stop** and surface it.

---

## Task 2: Add module-scope `SortableHeader` (without removing the nested copy yet)

We add the new module-scope component first and only delete the old nested copy in a later task. This keeps every intermediate state compilable, so you can run the type-checker between tasks if you want to.

Until the call sites are switched (Task 3), the new module-scope component is unused — that's fine; TypeScript will allow it, and the temporary state lasts a few minutes.

**Files:**
- Modify: `frontend/src/components/pages/Journal/JournalList.tsx` (insert between line 116 and line 118)

- [ ] **Step 1: Insert the new interface and component**

In `frontend/src/components/pages/Journal/JournalList.tsx`, find the end of the existing `JournalRow` declaration:

```tsx
    </td>
  </tr>
);

const JournalList: React.FC = () => {
```

Insert the following block **immediately before** `const JournalList: React.FC = () => {` (i.e. between the closing `);` of `JournalRow` on line 116 and the `const JournalList` line):

```tsx
interface SortableHeaderProps {
  column: string;
  sortBy: string;
  sortDescending: boolean;
  onSort: (column: string) => void;
  children: React.ReactNode;
}

const SortableHeader: React.FC<SortableHeaderProps> = ({
  column,
  sortBy,
  sortDescending,
  onSort,
  children,
}) => {
  const isActive = sortBy === column;
  const isAscending = isActive && !sortDescending;
  const isDescending = isActive && sortDescending;

  return (
    <th
      scope="col"
      className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider cursor-pointer hover:bg-gray-100 select-none"
      onClick={() => onSort(column)}
    >
      <div className="flex items-center space-x-1">
        <span>{children}</span>
        <div className="flex flex-col">
          <ChevronUp
            className={`h-3 w-3 ${isAscending ? "text-indigo-600" : "text-gray-300"}`}
          />
          <ChevronDown
            className={`h-3 w-3 -mt-1 ${isDescending ? "text-indigo-600" : "text-gray-300"}`}
          />
        </div>
      </div>
    </th>
  );
};

```

**Important details — do not deviate:**

- Use `React.FC<SortableHeaderProps>` (matches `JournalRow` in this same file; the arch review explicitly calls out that we keep the in-file style consistent and do not "improve" `JournalRow` while we're here).
- JSX, class names, icon usage, and div nesting must be byte-identical to the current nested version (lines 256–272), with the only two substitutions:
  - `sortBy === column` (was: closure `sortBy`) — same expression but now resolved from props.
  - `onClick={() => onSort(column)}` (was: `onClick={() => handleSort(column)}`).
- Leave one blank line before `const JournalList: React.FC = () => {`.

- [ ] **Step 2: Verify the file still type-checks**

From `frontend/`:
```bash
npm run build
```
Expected: exit code 0. (You'll get an unused-symbol warning at worst, depending on lint config — that's expected because Task 3 hasn't switched the call sites yet. A *type* error here is a real bug; stop and fix it before continuing.)

---

## Task 3: Update the three call sites to pass props

The three `<SortableHeader>` usages in the render are on lines 384–388 of the original file. After Task 2's insertion they shifted down by ~30 lines, but they are still trivially findable by `grep`.

**Files:**
- Modify: `frontend/src/components/pages/Journal/JournalList.tsx` (the three `<SortableHeader>` JSX call sites inside `JournalList`'s return)

- [ ] **Step 1: Replace the `title` header call site**

Find:
```tsx
                  <SortableHeader column="title">Název</SortableHeader>
```

Replace with:
```tsx
                  <SortableHeader
                    column="title"
                    sortBy={sortBy}
                    sortDescending={sortDescending}
                    onSort={handleSort}
                  >
                    Název
                  </SortableHeader>
```

- [ ] **Step 2: Replace the `entryDate` header call site**

Find:
```tsx
                  <SortableHeader column="entryDate">Datum</SortableHeader>
```

Replace with:
```tsx
                  <SortableHeader
                    column="entryDate"
                    sortBy={sortBy}
                    sortDescending={sortDescending}
                    onSort={handleSort}
                  >
                    Datum
                  </SortableHeader>
```

- [ ] **Step 3: Replace the `createdByUsername` header call site**

Find:
```tsx
                  <SortableHeader column="createdByUsername">
                    Autor
                  </SortableHeader>
```

Replace with:
```tsx
                  <SortableHeader
                    column="createdByUsername"
                    sortBy={sortBy}
                    sortDescending={sortDescending}
                    onSort={handleSort}
                  >
                    Autor
                  </SortableHeader>
```

- [ ] **Step 4: Verify no other `<SortableHeader>` call sites exist in the file**

Use the Grep tool with:
- pattern: `<SortableHeader`
- path: `frontend/src/components/pages/Journal/JournalList.tsx`
- output_mode: `content`
- `-n`: true

Expected: exactly **three** matches, all with the new prop set (`column=`, `sortBy=`, `sortDescending=`, `onSort=`). If you see fewer than three or any without the new props, you missed a call site — fix it before continuing.

- [ ] **Step 5: Verify the file still type-checks**

From `frontend/`:
```bash
npm run build
```
Expected: exit code 0. (At this point both the module-scope and nested `SortableHeader` exist; TypeScript will resolve the JSX usage to the *nested* one because it's in inner scope. That's fine — the type signature is structurally compatible with the new props since the nested version simply ignores extra props it doesn't destructure. The behavior remains identical to baseline. We delete the nested copy in Task 4.)

---

## Task 4: Delete the nested `SortableHeader` declaration

Now the call sites have the props they need from the module-scope component. Time to remove the duplicate nested declaration.

**Files:**
- Modify: `frontend/src/components/pages/Journal/JournalList.tsx` (delete lines that were 246–274 in the original; their numbers have shifted but the block is unmistakable)

- [ ] **Step 1: Delete the nested declaration and its comment**

Find this exact block inside the body of `JournalList` (it follows `handleCloseModal` and precedes `// Loading state`):

```tsx
  // Sortable header component
  const SortableHeader: React.FC<{
    column: string;
    children: React.ReactNode;
  }> = ({ column, children }) => {
    const isActive = sortBy === column;
    const isAscending = isActive && !sortDescending;
    const isDescending = isActive && sortDescending;

    return (
      <th
        scope="col"
        className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider cursor-pointer hover:bg-gray-100 select-none"
        onClick={() => handleSort(column)}
      >
        <div className="flex items-center space-x-1">
          <span>{children}</span>
          <div className="flex flex-col">
            <ChevronUp
              className={`h-3 w-3 ${isAscending ? "text-indigo-600" : "text-gray-300"}`}
            />
            <ChevronDown
              className={`h-3 w-3 -mt-1 ${isDescending ? "text-indigo-600" : "text-gray-300"}`}
            />
          </div>
        </div>
      </th>
    );
  };

```

Delete the whole block, including the leading `// Sortable header component` line and the trailing blank line, so that `handleCloseModal`'s closing brace is followed directly by `  // Loading state`.

After deletion, the surrounding region should look like:

```tsx
  const handleCloseModal = () => {
    setIsModalOpen(false);
    setEditingEntryId(null);
    // Refetch data after modal closes
    if (isSearchMode) {
      searchQuery.refetch();
    } else {
      entriesQuery.refetch();
    }
  };

  // Loading state
  if (loading) {
```

- [ ] **Step 2: Confirm zero `SortableHeader` declarations remain inside `JournalList`'s body**

Use the Grep tool with:
- pattern: `const SortableHeader`
- path: `frontend/src/components/pages/Journal/JournalList.tsx`
- output_mode: `content`
- `-n`: true

Expected: exactly **one** match, on the module-scope line you added in Task 2. If you see two, deletion didn't take — re-do Step 1.

- [ ] **Step 3: Confirm no closure reference to `handleSort` from outside `JournalList` exists**

Use the Grep tool with:
- pattern: `handleSort`
- path: `frontend/src/components/pages/Journal/JournalList.tsx`
- output_mode: `content`
- `-n`: true

Expected matches:
1. The `const handleSort = (column: string) => {` declaration inside `JournalList` (around the original line 197).
2. Three `onSort={handleSort}` references inside `JournalList`'s render (the three call sites from Task 3).

Total: **four** occurrences, all inside `JournalList`. Zero occurrences inside `SortableHeader`'s body — `SortableHeader` must reference only `onSort` (the prop), never `handleSort`.

---

## Task 5: Validate build, lint, and tests

Every check that was GREEN at baseline (Task 1) must still be GREEN.

**Files:** none modified.

- [ ] **Step 1: Run the build**

From `frontend/`:
```bash
npm run build
```
Expected: exit code 0. No new TypeScript errors. No new warnings.

If a "declared but never used" warning appears for any prop or variable, it indicates the refactor was incomplete — re-check Tasks 3 and 4.

- [ ] **Step 2: Run lint**

From `frontend/`:
```bash
npm run lint
```
Expected: exit code 0. No new errors. Warning count must equal the baseline established in Task 1 Step 3.

- [ ] **Step 3: Run the JournalList tests**

From `frontend/`:
```bash
npm test -- --watchAll=false JournalList
```
Expected: every test that passed at baseline still passes. No newly failing assertion, no new console warnings (especially no React `key` warnings or "component identity changed" advisories from the testing library).

- [ ] **Step 4: Final structural sanity grep**

Use the Grep tool with:
- pattern: `^const SortableHeader|^interface SortableHeaderProps`
- path: `frontend/src/components/pages/Journal/JournalList.tsx`
- output_mode: `content`
- `-n`: true

Expected: exactly **two** matches, both at column 0 (module scope) — the interface declaration and the `const SortableHeader` declaration.

---

## Task 6: Optional smoke test for sort interaction

The spec marks this as "encouraged but not required" and the arch review calls it a nice-to-have. Skip this task only if Task 1 Step 4 showed an existing test already asserts that clicking a header invokes the journal-entries hook with a new `sortBy` / `sortDirection`. Otherwise, add the smoke test below — it costs ~10 minutes and would catch any future regression that breaks sort wiring (e.g. someone removes a prop in the call site).

Before deciding, verify whether such coverage exists:

- [ ] **Step 1: Check whether sort-click coverage already exists**

Use the Grep tool with:
- pattern: `fireEvent\.click.*[Ss]ort|[Ss]ort.*fireEvent\.click|Název|Datum|Autor`
- path: `frontend/src/components/pages/Journal/__tests__/JournalList.test.tsx`
- output_mode: `content`
- `-n`: true

If you find a test that clicks a header and asserts the hook is called with new sort params, **skip Steps 2–4** and proceed to Task 7. Otherwise continue.

- [ ] **Step 2: Read the existing test file to learn its mocking pattern**

Read `frontend/src/components/pages/Journal/__tests__/JournalList.test.tsx` in full. Pay attention to:
- How `useJournalEntries` and `useSearchJournalEntries` are mocked (the mocked module path).
- The render helper / wrapper used (e.g. is there a `renderWithProviders`?).
- Whether the file uses `screen.getByRole('columnheader', { name: /…/ })` or text queries.

Mimic the existing style — do **not** introduce a new mocking helper or query convention.

- [ ] **Step 3: Add one smoke test that exercises sort wiring**

Add this test to `frontend/src/components/pages/Journal/__tests__/JournalList.test.tsx` (place it adjacent to other interaction-style tests in the file). Adapt the imports and mock surface to match what's already in the file — the snippet below shows the assertion shape, not the literal copy-paste import lines:

```tsx
test('clicking the "Datum" header asks the journal-entries hook to sort by EntryDate ascending', async () => {
  // Arrange — render with default state (sortBy="EntryDate", sortDescending=true)
  // The mock for useJournalEntries should be reset and re-spied so we can read its
  // most recent call arguments. Follow the existing test file's pattern.
  renderJournalList(); // use this file's existing render helper, whatever it is named

  // Act — click the "Datum" column header
  const dateHeader = screen.getByRole('columnheader', { name: /Datum/i });
  fireEvent.click(dateHeader);

  // Assert — because the column was already the active one, descending must flip to ascending.
  // The most recent call to useJournalEntries should reflect the new sort direction.
  await waitFor(() => {
    const lastCall = (useJournalEntries as jest.Mock).mock.calls.at(-1)?.[0];
    expect(lastCall).toMatchObject({
      sortBy: 'EntryDate',
      sortDirection: 'ASC',
    });
  });
});
```

**Do not** add tests for column-switching behavior, chevron orientation, or other branches — the spec is explicit that no new tests are required and that more than a smoke test is out of scope.

- [ ] **Step 4: Run the new test**

From `frontend/`:
```bash
npm test -- --watchAll=false JournalList
```
Expected: the new test passes alongside all existing tests. If the test fails because the mock surface differs from what the existing file uses, **adapt the test to match the file's pattern** rather than restructuring the file's mocks.

---

## Task 7: Manual smoke test in the dev server

Behavior must be visually identical to before. The verification checklist in the arch review requires this.

**Files:** none modified.

- [ ] **Step 1: Start the dev environment**

From repo root, follow `docs/development/setup.md` to start the BE + FE locally (typically `dotnet run` in the backend project and `npm start` in `frontend/`). Authenticate via the standard local dev flow.

- [ ] **Step 2: Navigate to the Journal page**

Open the Journal page in the browser. Confirm a list of entries renders (or the empty state, if your local data has no entries — in that case **stop**, seed at least two entries via the "Nový záznam" button, and continue).

- [ ] **Step 3: Click each sortable header**

For each of the three sortable columns — **Název**, **Datum**, **Autor**:
1. Click the header once.
2. Confirm the chevron of the clicked column highlights (active indigo color) in the expected orientation. The first click on a *new* column sorts ascending (ChevronUp lit). Clicking the *already-active* column flips direction.
3. Confirm the row order in the table updates to reflect the new sort.
4. Confirm no console errors or React warnings appear (open DevTools console).

- [ ] **Step 4: Quick regression sweep**

Perform one operation each from these adjacent flows; each must work exactly as before:
- Type text into the search box and click **Filtrovat** — results filter.
- Click **Vymazat** — filter clears, list resets.
- Change the page-size selector — pagination updates.
- Click a row — the edit modal opens.

If any of the above misbehaves, the refactor introduced a regression — re-check Tasks 3 and 4 for accidentally removed/renamed identifiers.

---

## Task 8: Commit

One commit. The change is atomic and easily summarized.

**Files:** all changes in `frontend/src/components/pages/Journal/JournalList.tsx` (and optionally `frontend/src/components/pages/Journal/__tests__/JournalList.test.tsx` if Task 6 was performed).

- [ ] **Step 1: Review the diff once more**

From repo root:
```bash
git diff -- frontend/src/components/pages/Journal/JournalList.tsx frontend/src/components/pages/Journal/__tests__/JournalList.test.tsx
```
Expected diff shape:
- In `JournalList.tsx`: an additive block (interface + component) between `JournalRow` and `JournalList`; a deletion block inside `JournalList`'s body removing the nested `SortableHeader`; three minor JSX call-site expansions adding `sortBy`, `sortDescending`, `onSort` props.
- In `JournalList.test.tsx` (only if Task 6 was performed): one new test added.
- **No** changes to imports, to `JournalRow`, to handlers, to state declarations, to pagination, modal, search, or filter code. If the diff shows anything else, revert those changes — they are out of scope (CLAUDE.md: "Surgical changes").

- [ ] **Step 2: Stage and commit**

From repo root:
```bash
git add frontend/src/components/pages/Journal/JournalList.tsx
# Only if Task 6 was performed:
git add frontend/src/components/pages/Journal/__tests__/JournalList.test.tsx
git commit -m "$(cat <<'EOF'
refactor(journal): move SortableHeader to module scope

SortableHeader was declared inside JournalList's function body, which
recreated its component identity on every render and forced React to
unmount/remount the table header subtree on unrelated state changes
(sort, pagination, modal). The header has no focusable descendants
today, so this is not user-visible, but the pattern is fragile and
breaks the convention set by the sibling JournalRow component in the
same file.

Moves SortableHeader and a named SortableHeaderProps interface to
module scope adjacent to JournalRow. Replaces closure capture of
sortBy, sortDescending, and handleSort with explicit props threaded
from JournalList. Visual output, sort-toggling behavior, and
chevron orientation are byte-equivalent to the prior implementation.

Note: the same anti-pattern exists in PurchaseOrderList, CatalogList,
ProductMarginsList, ManufactureInventoryList, InventoryList, and
GiftPackageManufacturingList. Out of scope for this PR (single-file
refactor per spec); flagged here as a candidate for a follow-up.
EOF
)"
```
Expected: commit succeeds, pre-commit hooks (if any) pass.

- [ ] **Step 3: Verify post-commit state**

From repo root:
```bash
git status
git log -1 --stat
```
Expected: working tree clean; the last commit touches only `JournalList.tsx` (and optionally `JournalList.test.tsx`); no other files crept in.

---

## Self-Review (notes for the executing engineer)

Before declaring the work done, mentally walk the spec one more time:

- **FR-1 (module-scope declaration):** Task 2 inserts it; Task 4 deletes the nested copy; Task 5 Step 4 greps to confirm uniqueness.
- **FR-2 (explicit props, no closure capture):** Task 3 updates call sites; Task 4 Step 3 greps to confirm `handleSort` is not referenced from inside `SortableHeader`.
- **FR-3 (behavior preserved):** Tasks 5 (tests), 6 (optional smoke test), and 7 (manual click-through) all verify behavior parity.
- **FR-4 (type safety, no `any`):** Task 2 introduces a named `SortableHeaderProps` interface with five explicit fields; Task 5 Step 1 confirms `npm run build` is clean.
- **NFR-1 (referential stability):** Achieved structurally by Task 2 + Task 4 — `SortableHeader` is now declared once at module load and never redeclared.
- **NFR-2 (consistency with `JournalRow`):** Task 2 places the new declaration adjacent to `JournalRow` and uses the same `React.FC<…Props>` typing style.
- **NFR-3 (existing tests stay green):** Task 1 baselines them; Task 5 re-verifies.

If you find a spec requirement that isn't covered by a task above, stop and add a task — do not silently extend the scope of an existing task to cover it.
