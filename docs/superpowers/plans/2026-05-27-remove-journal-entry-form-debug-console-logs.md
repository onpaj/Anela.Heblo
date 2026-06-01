# Remove Debug `console.log` Statements from `JournalEntryForm.tsx` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Delete three leftover debug `console.log` statements (prefixed with `🐛`) from `frontend/src/components/JournalEntryForm.tsx` that expose journal entry payloads to the browser console on every modal open and edit.

**Architecture:** Pure surgical deletion. No new files, no new abstractions, no replacement logger. The `useEffect` at lines 83–115 keeps its hook dependencies, its conditional branches, and all `setXxx` calls intact — only the three `console.log` statements are removed. No new tests; rely on the existing build + lint gates and a manual smoke test of the modal.

**Tech Stack:** React 18 + TypeScript, react-hook-style `useState`/`useEffect`, Create React App build pipeline (`npm run build`, `npm run lint`).

---

## File Structure

Single file modified. No new files.

```
frontend/src/components/JournalEntryForm.tsx   ← only file modified
```

**Responsibility of `JournalEntryForm.tsx`** (unchanged by this plan): controlled modal form for creating and editing journal entries — owns local form state (`title`, `content`, `entryDate`, `selectedTags`, `associatedProducts`), syncs from the `entry` prop on open/edit, validates, and submits via React Query mutations.

---

## Task 1: Delete the three debug `console.log` statements

**Files:**
- Modify: `frontend/src/components/JournalEntryForm.tsx:85`, `frontend/src/components/JournalEntryForm.tsx:88-94`, `frontend/src/components/JournalEntryForm.tsx:107`

**Context for the engineer (read first):**

The current `useEffect` looks like this verbatim (lines 83–115):

```tsx
  // Update form state when entry prop changes (for edit mode)
  useEffect(() => {
    console.log("🐛 JournalEntryForm useEffect - entry:", entry, "isEdit:", isEdit);
    if (entry?.entry) {
      const entryData = entry.entry;
      console.log("🐛 Updating form with entry data:", {
        title: entryData.title,
        content: entryData.content,
        entryDate: entryData.entryDate,
        tags: entryData.tags,
        products: entryData.associatedProducts
      });
      setTitle(entryData.title || "");
      setContent(entryData.content || "");
      setEntryDate(
        entryData.entryDate
          ? format(new Date(entryData.entryDate), "yyyy-MM-dd")
          : format(new Date(), "yyyy-MM-dd"),
      );
      setSelectedTags(
        entryData.tags?.map((tag) => tag.id!).filter((id) => id !== undefined) || [],
      );
      setAssociatedProducts(entryData.associatedProducts || []);
    } else if (!isEdit) {
      console.log("🐛 Resetting form for new entry");
      // Reset form for new entries
      setTitle("");
      setContent("");
      setEntryDate(format(new Date(), "yyyy-MM-dd"));
      setSelectedTags([]);
      setAssociatedProducts([]);
    }
  }, [entry, isEdit]);
```

After the edit, the entire `useEffect` must look like this verbatim:

```tsx
  // Update form state when entry prop changes (for edit mode)
  useEffect(() => {
    if (entry?.entry) {
      const entryData = entry.entry;
      setTitle(entryData.title || "");
      setContent(entryData.content || "");
      setEntryDate(
        entryData.entryDate
          ? format(new Date(entryData.entryDate), "yyyy-MM-dd")
          : format(new Date(), "yyyy-MM-dd"),
      );
      setSelectedTags(
        entryData.tags?.map((tag) => tag.id!).filter((id) => id !== undefined) || [],
      );
      setAssociatedProducts(entryData.associatedProducts || []);
    } else if (!isEdit) {
      // Reset form for new entries
      setTitle("");
      setContent("");
      setEntryDate(format(new Date(), "yyyy-MM-dd"));
      setSelectedTags([]);
      setAssociatedProducts([]);
    }
  }, [entry, isEdit]);
```

Three things changed and nothing else:
1. Line 85 (`console.log("🐛 JournalEntryForm useEffect ..."` ) is gone.
2. Lines 88–94 (the multi-line `console.log("🐛 Updating form with entry data:", { ... })` ) are gone — the next line after `const entryData = entry.entry;` is now `setTitle(entryData.title || "");`.
3. Line 107 (`console.log("🐛 Resetting form for new entry");`) is gone — the next line inside `else if (!isEdit)` is now the existing `// Reset form for new entries` comment.

Dependencies `[entry, isEdit]`, the `setTitle`/`setContent`/`setEntryDate`/`setSelectedTags`/`setAssociatedProducts` calls, the `// Reset form for new entries` comment, and surrounding indentation must remain byte-identical to the originals.

- [ ] **Step 1: Confirm starting state**

Run: `grep -n "console.log" frontend/src/components/JournalEntryForm.tsx`
Expected output (exactly three matches at lines 85, 88, 107):

```
85:    console.log("🐛 JournalEntryForm useEffect - entry:", entry, "isEdit:", isEdit);
88:      console.log("🐛 Updating form with entry data:", {
107:      console.log("🐛 Resetting form for new entry");
```

If the line numbers differ, re-read the file before editing — another change may have shifted them. Adjust the edits in subsequent steps to the actual content while preserving the same three statements as the deletion targets.

- [ ] **Step 2: Delete the first `console.log` (line 85)**

Edit `frontend/src/components/JournalEntryForm.tsx`. Replace:

```tsx
  // Update form state when entry prop changes (for edit mode)
  useEffect(() => {
    console.log("🐛 JournalEntryForm useEffect - entry:", entry, "isEdit:", isEdit);
    if (entry?.entry) {
```

with:

```tsx
  // Update form state when entry prop changes (for edit mode)
  useEffect(() => {
    if (entry?.entry) {
```

- [ ] **Step 3: Delete the multi-line `console.log` (lines 88–94)**

Edit `frontend/src/components/JournalEntryForm.tsx`. Replace:

```tsx
    if (entry?.entry) {
      const entryData = entry.entry;
      console.log("🐛 Updating form with entry data:", {
        title: entryData.title,
        content: entryData.content,
        entryDate: entryData.entryDate,
        tags: entryData.tags,
        products: entryData.associatedProducts
      });
      setTitle(entryData.title || "");
```

with:

```tsx
    if (entry?.entry) {
      const entryData = entry.entry;
      setTitle(entryData.title || "");
```

- [ ] **Step 4: Delete the third `console.log` (line 107)**

Edit `frontend/src/components/JournalEntryForm.tsx`. Replace:

```tsx
    } else if (!isEdit) {
      console.log("🐛 Resetting form for new entry");
      // Reset form for new entries
      setTitle("");
```

with:

```tsx
    } else if (!isEdit) {
      // Reset form for new entries
      setTitle("");
```

- [ ] **Step 5: Verify the file no longer contains the debug statements**

Run: `grep -n "console.log" frontend/src/components/JournalEntryForm.tsx`
Expected: no output (exit code 1). If any line is returned, re-inspect and remove it (only the three `🐛`-prefixed statements should ever have existed in this file — verify with `git diff frontend/src/components/JournalEntryForm.tsx` that you have removed only those three).

Also run: `grep -n "🐛" frontend/src/components/JournalEntryForm.tsx`
Expected: no output (exit code 1).

- [ ] **Step 6: Inspect the diff for collateral damage**

Run: `git diff frontend/src/components/JournalEntryForm.tsx`

Expected: exactly three removed regions. **All four** of these lines must still appear UNCHANGED in the file (they are *not* part of the diff):

```
      setTitle(entryData.title || "");
      setContent(entryData.content || "");
      setSelectedTags(
      setAssociatedProducts(entryData.associatedProducts || []);
```

The `useEffect` dependency array `}, [entry, isEdit]);` must be unchanged. The `// Reset form for new entries` comment must be present in the `else if (!isEdit)` branch.

If anything other than the three `console.log` statements was removed or altered, run `git checkout -- frontend/src/components/JournalEntryForm.tsx` and redo Steps 2–4.

- [ ] **Step 7: Run the linter**

Run from the repo root: `cd frontend && npm run lint`
Expected: completes without new errors or warnings attributable to `JournalEntryForm.tsx`. Pre-existing warnings in other files are acceptable; this change must not add any.

- [ ] **Step 8: Run the production build**

Run from the repo root: `cd frontend && npm run build`
Expected: build completes successfully (`Compiled successfully` or equivalent green output) with no TypeScript errors. If TypeScript reports an unused-variable warning for any of the previously-logged identifiers (none of them are local — they are all props or destructured fields used elsewhere in the same `useEffect`), re-inspect the diff for accidental over-deletion.

- [ ] **Step 9: Run the existing component test suite touched by this file**

Run from the repo root: `cd frontend && npx jest JournalEntryForm --passWithNoTests`
Expected: passes (or `No tests found` — there is no dedicated test file for this component today, which is acceptable per the spec). If any existing test fails, investigate before proceeding.

- [ ] **Step 10: Manual smoke test in the browser**

This is a UI change — verify in a running app per the project's "for UI or frontend changes, start the dev server and use the feature in a browser" rule. Either:

(a) Start the local dev server: `cd frontend && npm start` and open the journal page in the browser.
(b) Or, if the worktree cannot run a dev server, rely on the existing nightly E2E suite plus the verification in Steps 7–9 and explicitly note in the PR description that the dev server could not be started in this environment.

If running locally, perform these checks with DevTools console open:

1. Open the journal page and click "Nový záznam" (new entry). Verify: the modal opens with empty fields. The console shows **no** `🐛`-prefixed messages.
2. Close the modal, then click an existing entry to edit it. Verify: the modal opens with the entry's title, content, date, tags, and associated products populated. The console shows **no** `🐛`-prefixed messages.
3. Save an edit. Verify: the modal closes and the entry list reflects the change.
4. Open a new-entry modal again. Verify: fields are reset to defaults.

Any deviation from the above behavior is a regression — `git checkout -- frontend/src/components/JournalEntryForm.tsx` and revisit Steps 2–4.

- [ ] **Step 11: Commit**

```bash
git add frontend/src/components/JournalEntryForm.tsx
git commit -m "chore: remove debug console.log statements from JournalEntryForm

Three 🐛-prefixed console.log calls inside the useEffect that syncs
form state from the entry prop were leftover scaffolding. They printed
the full journal entry payload (title, content, tags, associated
products) to the browser console on every modal open and edit,
exposing potentially sensitive business notes to anyone with DevTools
open. Form behavior is unchanged."
```

- [ ] **Step 12: Post-commit verification**

Run: `git log -1 --stat`
Expected: one commit, one file changed (`frontend/src/components/JournalEntryForm.tsx`), with ~9 deletions and 0 insertions (3 single-line deletions + 7-line multi-line deletion = 10 lines removed total; the precise number depends on how the formatter counts the multi-line statement's trailing brace, but it should be a deletions-only diff in the range 9–11 lines).

If insertions > 0, investigate — the change must be a pure deletion.

---

## Self-Review

**1. Spec coverage:**
- FR-1 (remove line 85 log) → Task 1 Step 2.
- FR-2 (remove lines 88–94 log) → Task 1 Step 3.
- FR-3 (remove line 107 log) → Task 1 Step 4.
- FR-4 (preserve behavior) → Task 1 Steps 6, 7, 8, 9, 10.
- NFR-1 (performance) → no measurable change; no task required.
- NFR-2 (security) → satisfied by deletion; Step 10 verifies no console output.
- NFR-3 (maintainability) → satisfied by deletion.
- NFR-4 (compatibility) → no contract changes; Step 6 verifies diff scope.
- Out-of-scope items (no logger, no `no-console` rule, no new tests) → explicitly not in plan.

All spec items covered.

**2. Placeholder scan:** No "TBD", "implement later", or "similar to" references. Every step shows exact commands or exact before/after code blocks.

**3. Type consistency:** No new types, functions, or methods introduced. Existing identifiers (`entry`, `isEdit`, `entryData`, `setTitle`, `setContent`, `setEntryDate`, `setSelectedTags`, `setAssociatedProducts`) used in before/after blocks match the file verbatim.
