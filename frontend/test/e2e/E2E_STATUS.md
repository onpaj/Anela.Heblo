# E2E Test Status Map

> **Purpose**: Orchestration tracking for the "make E2E tests usable" effort.
> **Target**: deployed staging `https://heblo.stg.anela.cz` (nightly gate, not PR CI).
> **Run a single test**: `./scripts/run-playwright-tests.sh <module> "<test name>"`

## Legend

| Mark | Meaning |
| ---- | ------- |
| ⬜ | Not yet triaged |
| ✅ | Passing (verified) |
| 🔧 | Test was invalid → fixed |
| 🐛 | **Valid test exposing a real application bug** — test left failing/skipped, bug NOT fixed (out of scope) |
| 🗑️ | Test no longer meaningful → deleted |
| ⏭️ | Pre-existing `.skip` inherited from earlier effort |

## Application bugs found (do not fix — report only)

_None recorded yet._

---

## Module status

| Module | Spec files | Tests | Status |
| ------ | ---------- | ----- | ------ |
| baleni | 1 | 5 | ⬜ |
| catalog | 9 | 84 | ⬜ |
| core | 8 | 77 | ⬜ |
| finance | 1 | 3 | ⬜ |
| issued-invoices | 5 | 29 | ⬜ |
| leaflet-generator | 1 | 4 | ⬜ |
| manufacturing | 5 | 9 | ⬜ |
| marketing | 5 | 36 | ⬜ |
| stock-operations | 9 | 57 | ⬜ |
| terminal | 1 | 5 | ⬜ |
| transport | 7 | 42 | ⬜ |

---

## Per-file tracking

Filled in from the baseline run.

| Spec file | Result | Notes |
| --------- | ------ | ----- |
| baleni/packing.spec.ts | ⬜ | |
| catalog/clear-filters.spec.ts | ⬜ | |
| catalog/combined-filters.spec.ts | ⬜ | |
| catalog/filter-edge-cases.spec.ts | ⬜ | |
| catalog/margins-chart.spec.ts | ⬜ | |
| catalog/pagination-with-filters.spec.ts | ⬜ | |
| catalog/product-type-filter.spec.ts | ⬜ | |
| catalog/sorting-with-filters.spec.ts | ⬜ | |
| catalog/text-search-filters.spec.ts | ⬜ | |
| catalog/ui.spec.ts | ⬜ | |
| core/changelog.spec.ts | ⬜ | |
| core/dashboard.spec.ts | ⬜ | |
| core/invoice-classification-history-actions.spec.ts | ⬜ | |
| core/invoice-classification-history-filters.spec.ts | ⬜ | |
| core/invoice-classification-history.spec.ts | ⬜ | |
| core/recurring-jobs-management.spec.ts | ⬜ | |
| core/sidebar-navigation.spec.ts | ⬜ | |
| core/staging-auth.spec.ts | ⬜ | |
| finance/financial-overview-mobile.spec.ts | ⬜ | |
| issued-invoices/filters.spec.ts | ⬜ | |
| issued-invoices/navigation.spec.ts | ⬜ | |
| issued-invoices/pagination.spec.ts | ⬜ | |
| issued-invoices/sorting.spec.ts | ⬜ | |
| issued-invoices/status-badges.spec.ts | ⬜ | |
| leaflet-generator/leaflet-doc-management.spec.ts | ⬜ | |
| manufacturing/batch-planning-error-handling.spec.ts | ⬜ | |
| manufacturing/batch-planning-workflow.spec.ts | ⬜ | |
| manufacturing/order-creation.spec.ts | ⬜ | |
| manufacturing/order-state-return.spec.ts | ⬜ | |
| manufacturing/protocol.spec.ts | ⬜ | |
| marketing/calendar-view.spec.ts | ⬜ | |
| marketing/create-record.spec.ts | ⬜ | |
| marketing/grid-view.spec.ts | ⬜ | |
| marketing/loading.spec.ts | ⬜ | |
| marketing/mobile-agenda.spec.ts | ⬜ | |
| stock-operations/accept.spec.ts | ⬜ | |
| stock-operations/badges.spec.ts | ⬜ | |
| stock-operations/filters.spec.ts | ⬜ | |
| stock-operations/navigation.spec.ts | ⬜ | |
| stock-operations/panel.spec.ts | ⬜ | |
| stock-operations/retry.spec.ts | ⬜ | |
| stock-operations/sorting.spec.ts | ⬜ | |
| stock-operations/source-filter.spec.ts | ⬜ | |
| stock-operations/state-filter.spec.ts | ⬜ | |
| terminal/lot-identification.spec.ts | ⬜ | |
| transport/box-creation.spec.ts | ⬜ | |
| transport/box-items.spec.ts | ⬜ | |
| transport/box-management.spec.ts | ⬜ | |
| transport/box-receive.spec.ts | ⬜ | |
| transport/box-workflow.spec.ts | ⬜ | |
| transport/boxes-basic.spec.ts | ⬜ | |
| transport/ean-integration.spec.ts | ⬜ | |
