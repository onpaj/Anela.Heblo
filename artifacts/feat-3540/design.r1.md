# Design: Fix Stock Operations E2E — page never renders rows or empty state

## Skip Design: true

The architecture review (`arch-review.r1.md`) determined `Skip Design: true` for this
feature: the root cause is a missing role-claim grant for the E2E synthetic test
identity plus a broken E2E test assertion — a permission-configuration and
test-hardening fix with **no new or changed UI components, screens, layouts, or
visual design decisions**. `StockOperationsPage.tsx` and its existing loading/error/
empty/data render states were reviewed by both the analyst and the architect and
require no changes.

No design document is produced for this feature. Proceed directly to planning using
`spec.r1.md` and `arch-review.r1.md`.
