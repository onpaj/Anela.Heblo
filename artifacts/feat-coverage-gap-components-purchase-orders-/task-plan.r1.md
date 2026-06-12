Plan saved to `docs/superpowers/plans/2026-06-11-purchase-order-validation-unit-tests.md`.

**Summary:**
- 9 tasks covering the 5 spec requirements plus a final coverage/build/lint gate.
- All 4 architecture-review amendments are baked in: `__tests__/` directory placement, `selectedSupplier` field name, empty-string + `undefined` date cases (no `null`), whitespace-material branch test, and `clearFieldError` empty-string-value test.
- Conventions locked from the sibling test file: `Object.assign(new PurchaseOrderLineDto(), {...})`, `YYYY-MM-DD` strings, local factory helpers.
- Each task is a focused arrange-act-assert block with the exact code, the exact `npm test` invocation, the expected passing count, and a conventional commit message.
- Self-review confirms every FR/NFR maps to a task, no placeholders, and type/identifier consistency across tasks.