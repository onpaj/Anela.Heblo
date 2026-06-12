## Module / File
`backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/DuplicateManufactureOrder/DuplicateManufactureOrderHandler.cs`

## Coverage
The Manufacture module has 64 tests, but this handler is only referenced in `ManufactureOrderControllerTests.cs`, which mocks the mediator and does not execute the handler body. Business logic is entirely untested.

## What's not tested
Key behaviors in the duplicate creation:
1. **Source order not found** — returns `OrderNotFound` error; the duplicate is never created
2. **SemiProduct null guard** — if `sourceOrder.SemiProduct == null`, no semi-product is attached to the duplicate; the `if` condition silently skips it; the caller receives a valid response with no indication
3. **Quantity reset** — both semi-product and product `ActualQuantity` are reset to `PlannedQuantity` (not to the source's actual quantity); this is intentional but unverified
4. **Lot number and expiration generation** — `GetDefaultLot` and `GetDefaultExpiration` are called with `_timeProvider.GetUtcNow()` and `ExpirationMonths`; the resulting values on the created order are never asserted

## Why it matters
If the `SemiProduct` null guard is accidentally removed or its condition inverted, duplicated orders silently lose their semi-product definition. The `ActualQuantity = PlannedQuantity` reset ensures fresh orders start from plan — if someone changes it to copy the source actual, the production team gets incorrectly pre-filled quantities with no test failing.

## Suggested approach
Unit-test with a mocked `IManufactureOrderRepository`, `ICurrentUserService`, and a `FakeTimeProvider`. Three tests: source not found; source with semi-product (assert quantities reset and lot/expiry values); source without semi-product (assert no semi-product on duplicate). ~1.5 hours.

---
_Filed by weekly coverage-gap routine on 2026-06-08. Based on CI run #27104028537 (6568feba33640ae063b2cb6af3c81da31b3720e1)._