# Implementation: backend-available-transitions (r1)

- Added `AvailableTransitions` (List<ManufactureOrderState>) to `GetManufactureOrderResponse`.
- `GetManufactureOrderHandler` populates it from `order.CanTransitionTo`, excluding current state, ordered by enum value.
- Tests: Theory over all 5 states asserting legal set; not-found response yields empty list.
- Verified: `dotnet build` (Application, Tests) 0 errors; `GetManufactureOrderHandlerTests` 13/13 pass; `dotnet format` run on touched files.
