# Impl frontend-client-helper r1

- Regenerated OpenAPI TS client (dotnet build API, dotnet tool restore, GenerateFrontendClientManual). Diff limited to CreatePurchaseOrderRequest: orderDate?: Date, expectedDeliveryDate?: Date (init/toJSON/interface).
- PurchaseOrderHelpers.createPurchaseOrderRequest builds Dates as UTC midnight (value + "T00:00:00Z").
- usePurchaseOrders.ts needed no change (tsc: no src errors).
- Added 2 jest tests (serialized date equals input date; empty expected date stays undefined).
- Verify: purchase-orders jest 47/47 pass, eslint clean, tsc no src errors. Full `npm run build` not run (sandbox node_modules typings for react-i18next fail tsc, unrelated).
