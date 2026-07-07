## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductMarginsHandlerTests.cs:195` — `Handle_UnknownSortField_FallsBackToProductCodeAscending` asserts both `BeInAscendingOrder()` and the three explicit index checks (`[0]`, `[1]`, `[2]`). The index checks are redundant given `BeInAscendingOrder()` already covers order; removing them would make the test slightly less brittle if product codes change in future.
