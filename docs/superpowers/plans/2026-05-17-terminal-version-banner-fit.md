# Terminal Version Banner Fit — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the "New Version Available" toast fit on narrow viewports (≤ 640 px, e.g. the `/terminal` route) without overflowing horizontally.

**Architecture:** Replace the hardcoded `min-w-[600px]` on `ToastContainer`'s fixed wrapper with a responsive Tailwind class set: full-bleed (left-4 … right-4) on small screens, and the original right-anchored 600 px width on `sm` and above. No changes to `Toast.tsx` — it already uses `w-full max-w-2xl` which adapts to its container.

**Tech Stack:** React 18, Tailwind CSS, Jest + React Testing Library

---

## File Map

| Action  | Path |
|---------|------|
| Modify  | `frontend/src/components/ui/ToastContainer.tsx:14` |
| Create  | `frontend/src/components/ui/__tests__/ToastContainer.test.tsx` |

---

### Task 1: Write the failing test for responsive container classes

**Files:**
- Create: `frontend/src/components/ui/__tests__/ToastContainer.test.tsx`

- [ ] **Step 1: Write the failing test**

`createPortal` renders outside the React tree; mock it so the test can inspect the rendered output.

```tsx
import React from 'react';
import { render } from '@testing-library/react';
import * as ReactDOM from 'react-dom';
import ToastContainer from '../ToastContainer';

jest.spyOn(ReactDOM, 'createPortal').mockImplementation((node) => node as React.ReactPortal);

describe('ToastContainer', () => {
  it('uses responsive classes — full-bleed on mobile, right-anchored on sm+', () => {
    const toast = {
      id: '1',
      type: 'info' as const,
      title: 'Test toast',
      onClose: jest.fn(),
    };

    const { container } = render(
      <ToastContainer toasts={[toast]} onClose={jest.fn()} />,
    );

    const wrapper = container.firstChild as HTMLElement;
    expect(wrapper.className).toContain('left-4');
    expect(wrapper.className).toContain('sm:left-auto');
    expect(wrapper.className).toContain('sm:min-w-[600px]');
    expect(wrapper.className).not.toContain('min-w-[600px]');
  });
});
```

- [ ] **Step 2: Run the test — it must fail**

```bash
cd frontend && npx jest src/components/ui/__tests__/ToastContainer.test.tsx --no-coverage
```

Expected output: `FAIL` — assertion `not.toContain('min-w-[600px]')` fails because the current class string contains `min-w-[600px]` without the `sm:` prefix.

---

### Task 2: Apply the responsive fix to ToastContainer

**Files:**
- Modify: `frontend/src/components/ui/ToastContainer.tsx:14`

- [ ] **Step 1: Edit the container className**

In `frontend/src/components/ui/ToastContainer.tsx`, change line 14 from:

```tsx
<div className="fixed top-4 right-4 z-50 space-y-4 min-w-[600px]">
```

to:

```tsx
<div className="fixed top-4 right-4 left-4 z-50 space-y-4 sm:left-auto sm:min-w-[600px]">
```

- [ ] **Step 2: Run the test — it must pass**

```bash
cd frontend && npx jest src/components/ui/__tests__/ToastContainer.test.tsx --no-coverage
```

Expected output: `PASS` — all assertions green.

- [ ] **Step 3: Run build and lint**

```bash
cd frontend && npm run build && npm run lint
```

Expected output: no errors.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/components/ui/ToastContainer.tsx \
        frontend/src/components/ui/__tests__/ToastContainer.test.tsx
git commit -m "fix: make toast container responsive on narrow viewports"
```
