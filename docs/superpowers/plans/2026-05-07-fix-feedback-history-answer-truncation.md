# Fix Feedback History Answer Truncation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the `.slice(0, 120)` truncation from KB and leaflet feedback adapters so the full answer/markdown text reaches the "Detail záznamu" modal.

**Architecture:** Two frontend adapter hooks map API response rows into `FeedbackDetail` objects. The `secondaryText` field (shown as ODPOVĚĎ / Výstup in the modal) was being sliced to 120 characters — a pointless guard since the table never renders `secondaryText`, but the modal does. The fix removes the slice from both adapters, matching the existing behaviour of the articles adapter.

**Tech Stack:** TypeScript, React, Vite (`npm run build`), ESLint (`npm run lint`). No backend changes.

---

## File Map

| Action | File |
|--------|------|
| Modify | `frontend/src/components/feedback/adapters/useKbFeedbackAdapter.ts` — line 17 |
| Modify | `frontend/src/components/feedback/adapters/useLeafletFeedbackAdapter.ts` — line 17 |

---

### Task 1: Remove truncation from KB feedback adapter

**Files:**
- Modify: `frontend/src/components/feedback/adapters/useKbFeedbackAdapter.ts:17`

- [ ] **Step 1: Open the file and locate the truncation**

Read `frontend/src/components/feedback/adapters/useKbFeedbackAdapter.ts`.
Confirm line 17 reads:
```ts
secondaryText: (log.answer ?? '').slice(0, 120),
```

- [ ] **Step 2: Remove the slice**

Change line 17 to:
```ts
secondaryText: log.answer ?? '',
```

The full mapping block should now look like:
```ts
const rows: FeedbackDetail[] = (query.data?.logs ?? []).map((log) => ({
  id: log.id,
  primaryText: log.question,
  secondaryText: log.answer ?? '',
  createdAt: log.createdAt,
  userId: log.userId ?? undefined,
  precisionScore: log.precisionScore,
  styleScore: log.styleScore,
  hasFeedback: log.hasFeedback,
  feedbackComment: log.feedbackComment,
}));
```

- [ ] **Step 3: Verify TypeScript compiles**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend
npm run build 2>&1 | tail -20
```

Expected: build succeeds with no TypeScript errors related to this file.

- [ ] **Step 4: Commit**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
git add frontend/src/components/feedback/adapters/useKbFeedbackAdapter.ts
git commit -m "fix(feedback): remove 120-char truncation from KB answer in feedback adapter"
```

---

### Task 2: Remove truncation from leaflet feedback adapter

**Files:**
- Modify: `frontend/src/components/feedback/adapters/useLeafletFeedbackAdapter.ts:17`

- [ ] **Step 1: Open the file and locate the truncation**

Read `frontend/src/components/feedback/adapters/useLeafletFeedbackAdapter.ts`.
Confirm line 17 reads:
```ts
secondaryText: (item.finalMarkdown ?? '').slice(0, 120),
```

- [ ] **Step 2: Remove the slice**

Change line 17 to:
```ts
secondaryText: item.finalMarkdown ?? '',
```

The full mapping block should now look like:
```ts
const rows: FeedbackDetail[] = (query.data?.items ?? []).map((item) => ({
  id: item.id,
  primaryText: item.topic,
  secondaryText: item.finalMarkdown ?? '',
  createdAt: item.createdAt,
  userId: item.userId ?? undefined,
  precisionScore: item.precisionScore,
  styleScore: item.styleScore,
  hasFeedback: item.hasFeedback,
  feedbackComment: item.feedbackComment,
}));
```

- [ ] **Step 3: Verify build and lint**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend
npm run build 2>&1 | tail -20
npm run lint 2>&1 | tail -20
```

Expected: clean build, no lint errors.

- [ ] **Step 4: Commit**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
git add frontend/src/components/feedback/adapters/useLeafletFeedbackAdapter.ts
git commit -m "fix(feedback): remove 120-char truncation from leaflet finalMarkdown in feedback adapter"
```

---

### Task 3: Final verification

- [ ] **Step 1: Full build + lint pass**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend
npm run build && npm run lint
```

Expected output ends with no errors.

- [ ] **Step 2: Manual smoke-test checklist (when running locally)**

1. Navigate to **Marketing Feedback → Poradenství (KB)** tab.
2. Click a record with a long answer (any record from 06.05.2026 fits).
3. Confirm **ODPOVĚĎ** shows the full text — no mid-word cutoff like `[Ochráním bříšk`.
4. Switch to **Letáky** tab. Open any generation record.
5. Confirm **Výstup** shows full markdown — not capped at ~120 chars.
6. Switch to **Články** tab. Open any record — confirm no regression (articles never sliced; this is a control check).
7. Confirm the list table on all three tabs still looks identical — `secondaryText` is not rendered in the list, so the table should be visually unchanged.
