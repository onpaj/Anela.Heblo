# Manufacture Conditions Tab Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the "Podmínky výroby" production conditions table out of the "Základní informace" tab and into a new third tab in the manufacture order detail modal.

**Architecture:** All changes are purely UI — two files in `frontend/src/components/manufacture/`. No backend or API changes. The `ConditionsReadingsSection` component stays unchanged in its own file; only its placement within the tab layout changes.

**Tech Stack:** React, TypeScript, lucide-react icons, Tailwind CSS

---

### Task 1: Add `"conditions"` to activeTab state and import Thermometer icon

**Files:**
- Modify: `frontend/src/components/manufacture/pages/ManufactureOrderDetail.tsx:4-13,71`

- [ ] **Step 1: Extend the activeTab union type**

In `ManufactureOrderDetail.tsx` line 71, change:
```ts
const [activeTab, setActiveTab] = useState<"info" | "notes" | "log">("info");
```
to:
```ts
const [activeTab, setActiveTab] = useState<"info" | "notes" | "log" | "conditions">("info");
```

- [ ] **Step 2: Add Thermometer to the lucide-react import**

In `ManufactureOrderDetail.tsx` lines 4–13, add `Thermometer` to the lucide-react import:
```ts
import {
  Loader2,
  AlertCircle,
  Edit,
  Info,
  StickyNote,
  Thermometer,
  Factory,
  ArrowLeft,
  X,
} from "lucide-react";
```

- [ ] **Step 3: Run TypeScript check to confirm no errors so far**

```bash
cd frontend && npx tsc --noEmit 2>&1 | head -30
```
Expected: no new errors related to `activeTab` or `Thermometer`.

---

### Task 2: Add the "Podmínky výroby" tab button

**Files:**
- Modify: `frontend/src/components/manufacture/pages/ManufactureOrderDetail.tsx:604-615`

- [ ] **Step 1: Add third tab button after the Poznámky button**

After the closing `</button>` of the Poznámky tab (after line 614), insert:
```tsx
<button
  onClick={() => setActiveTab("conditions")}
  className={`${
    activeTab === "conditions"
      ? "border-indigo-500 text-indigo-600"
      : "border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300"
  } whitespace-nowrap py-2 px-1 border-b-2 font-medium text-sm flex items-center`}
>
  <Thermometer className="h-4 w-4 mr-2" />
  Podmínky výroby
</button>
```

---

### Task 3: Move ConditionsReadingsSection to the new tab

**Files:**
- Modify: `frontend/src/components/manufacture/pages/ManufactureOrderDetail.tsx:666-677`

- [ ] **Step 1: Remove ConditionsReadingsSection from the info tab**

Delete line 666:
```tsx
<ConditionsReadingsSection readings={order?.conditionsReadings ?? []} />
```
(It is the last element inside the `activeTab === "info"` block, right before the closing `</>` fragment tag.)

- [ ] **Step 2: Add new conditions tab content block**

After the closing `}` of the `{activeTab === "notes" && ...}` block (after line 677), add:
```tsx
{activeTab === "conditions" && (
  <ConditionsReadingsSection readings={order?.conditionsReadings ?? []} />
)}
```

---

### Task 4: Tighten ConditionsReadingsSection spacing (optional cosmetic)

**Files:**
- Modify: `frontend/src/components/manufacture/detail/ConditionsReadingsSection.tsx:50`

- [ ] **Step 1: Remove top margin from wrapper div**

On line 50, change:
```tsx
<div className="bg-gray-50 rounded-lg p-3 mt-4">
```
to:
```tsx
<div className="bg-gray-50 rounded-lg p-3">
```
Rationale: `mt-4` existed to push the section below the grid above it. Now it is the only content in its tab, so the margin is visual noise.

---

### Task 5: Build and commit

**Files:** All modified files from Tasks 1–4

- [ ] **Step 1: Run full TypeScript check**

```bash
cd frontend && npx tsc --noEmit 2>&1
```
Expected: 0 errors.

- [ ] **Step 2: Run frontend build**

```bash
cd frontend && npm run build 2>&1 | tail -20
```
Expected: Build succeeds with no errors.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/manufacture/pages/ManufactureOrderDetail.tsx \
        frontend/src/components/manufacture/detail/ConditionsReadingsSection.tsx
git commit -m "feat(manufacture): move podmínky výroby to separate tab in order detail"
```
