Plan saved to `docs/superpowers/plans/2026-06-05-getpackingorder-shipping-address-fields.md`.

Summary of the plan (5 tasks, TDD-first, surgical):

1. **Add nullable properties to `GetPackingOrderResponse`** — extend the existing positive-path test first (RED), then add `ShippingStreet/City/Zip` as `string?` on the DTO.
2. **Map the three fields in `GetPackingOrderHandler`** — three additional assignments in the existing object initializer; turns the test GREEN.
3. **Add null-path assertions** — extend the existing minimal eligibility test to assert nulls flow through unchanged (covers the no-delivery-address case).
4. **Regenerate the TypeScript client** — `dotnet build` + `dotnet format` + `npm run build` + `npm run lint`; verify the diff in `frontend/src/api/generated/api-client.ts` contains the three new field names.
5. **Final validation gates + commit** — full backend rebuild, full test class re-run, then a single `feat:` commit covering the 4 touched files.

The plan stays inside one vertical slice, follows the project rules (DTOs as classes, nullable strings mirroring the adapter, no record), and respects every out-of-scope constraint from the spec.