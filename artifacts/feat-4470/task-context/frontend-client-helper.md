### task: frontend-client-helper
Regenerate the client and fix the create mapping (FR-4). Depends on backend-handler.
- Build the backend/regenerate the OpenAPI TS client per `docs/development/api-client-generation.md`; commit only the expected diff in `frontend/src/api/generated/api-client.ts` (`orderDate: Date`, `expectedDeliveryDate?: Date`).
- `frontend/src/components/purchase-orders/form/PurchaseOrderHelpers.tsx`: build `Date` from the form `YYYY-MM-DD` as UTC midnight (`new Date(value + "T00:00:00Z")`); fix any call site in `frontend/src/api/hooks/usePurchaseOrders.ts` if types break.
- Add a FE unit test confirming the serialized calendar date equals the input date.
- Verify: `npm run build`, `npm run lint`, related jest tests pass.

