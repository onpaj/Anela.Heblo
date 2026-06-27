# Task Plan: Dark Mode Fix – RecurringJobsPage Happy-Path Render

## Overview

Two Tailwind `dark:` variants are missing from the happy-path render branch of `RecurringJobsPage.tsx`. The fix adds `dark:text-graphite-text` to the page heading and `dark:bg-graphite-surface dark:shadow-soft-dark` to the main content card. No new files, no structural changes.

### task: add-dark-mode-variants-recurringjobspage

**Goal:** Make the page heading and main content card respect dark mode on the happy-path render branch.

**Files to change:**
- `frontend/src/pages/RecurringJobsPage.tsx` — add two `dark:` Tailwind variants (lines 169 and 173)

**Implementation steps:**
1. On line 169, change the `<h1>` className from `"text-lg font-semibold text-gray-900"` to `"text-lg font-semibold text-gray-900 dark:text-graphite-text"`.
2. On line 173, change the wrapping `<div>` className from `"flex-1 bg-white shadow rounded-lg overflow-hidden flex flex-col min-h-0"` to `"flex-1 bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark rounded-lg overflow-hidden flex flex-col min-h-0"`.

**Acceptance criteria:**
- `frontend/src/pages/RecurringJobsPage.tsx` line 169 contains `dark:text-graphite-text`.
- `frontend/src/pages/RecurringJobsPage.tsx` line 173 contains both `dark:bg-graphite-surface` and `dark:shadow-soft-dark`.
- `npm run build` passes with no errors.
- `npm run lint` passes with no warnings introduced by this change.
