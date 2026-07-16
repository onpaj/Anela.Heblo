# Task Plan: Inject TimeProvider into three Manufacture handlers

### task: inject-timeprovider-manufacture-handlers
Inject `TimeProvider` into `GetManufactureProtocolHandler`, `ResolveManualActionHandler`, and `GetSemiproductRecipePdfHandler`, replacing their direct `DateTime.UtcNow`/`DateTime.Now` calls, and update the three corresponding unit test files so the suite compiles and passes. This is one cohesive, mechanical change applying a single established pattern across three files plus their tests — implement and verify together.

**Reference pattern (read, do not modify):**
`backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrderStatus/UpdateManufactureOrderStatusHandler.cs` — field `private readonly TimeProvider _timeProvider;`, constructor parameter `TimeProvider timeProvider`, assignment `_timeProvider = timeProvider;`, usage `_timeProvider.GetUtcNow().DateTime`. Its test, `UpdateManufactureOrderStatusHandlerTests.cs`, passes `TimeProvider.System` to the constructor — follow that same test convention.

**Files to change (production):**
1. `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol/GetManufactureProtocolHandler.cs`
   - Add `private readonly TimeProvider _timeProvider;` field.
   - Append `TimeProvider timeProvider` as the **last** constructor parameter; assign `_timeProvider = timeProvider;`.
   - Line 85: replace `GeneratedAt = DateTime.UtcNow,` with `GeneratedAt = _timeProvider.GetUtcNow().DateTime,`.
   - No other line changes.
2. `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/ResolveManualAction/ResolveManualActionHandler.cs`
   - Add `private readonly TimeProvider _timeProvider;` field.
   - Append `TimeProvider timeProvider` as the last constructor parameter; assign `_timeProvider = timeProvider;`.
   - Line 54: replace `order.ErpDiscardResidueDocumentNumberDate = DateTime.UtcNow;` with `order.ErpDiscardResidueDocumentNumberDate = _timeProvider.GetUtcNow().DateTime;`.
   - Line 66: replace `CreatedAt = DateTime.UtcNow,` with `CreatedAt = _timeProvider.GetUtcNow().DateTime,`.
   - No other line changes.
3. `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetSemiproductRecipePdf/GetSemiproductRecipePdfHandler.cs`
   - Add `private readonly TimeProvider _timeProvider;` field.
   - Append `TimeProvider timeProvider` as the last constructor parameter; assign `_timeProvider = timeProvider;`.
   - Line 65: replace `PrintedAt = DateTime.Now,` with `PrintedAt = _timeProvider.GetUtcNow().DateTime,` (this also fixes the local-time-vs-UTC bug — intended per spec FR-3).
   - No other line changes.

**Files to change (tests):**
4. `backend/test/Anela.Heblo.Tests/Features/Manufacture/GetManufactureProtocolHandlerTests.cs`
5. `backend/test/Anela.Heblo.Tests/Features/Manufacture/ResolveManualActionHandlerTests.cs`
6. `backend/test/Anela.Heblo.Tests/Features/Manufacture/GetSemiproductRecipePdfHandlerTests.cs`
   - Update handler construction call site(s) in each file to pass `TimeProvider.System` (matching `UpdateManufactureOrderStatusHandlerTests.cs`'s convention) as the new last constructor argument.
   - A fake/fixed `TimeProvider` may be used instead of `TimeProvider.System` in a given test if the implementer wants to assert an exact timestamp value, but this is optional — not required to satisfy acceptance criteria.
   - Do not change unrelated test logic or assertions beyond what's needed to keep them compiling/passing.

**Before changing, confirm no missed call sites:** search the repo for `new GetManufactureProtocolHandler(`, `new ResolveManualActionHandler(`, and `new GetSemiproductRecipePdfHandler(` — expected to find only the three test files (production resolution goes through MediatR's container). Update any additional call site found.

**Acceptance criteria:**
- All three handlers (`GetManufactureProtocolHandler`, `ResolveManualActionHandler`, `GetSemiproductRecipePdfHandler`) have a `private readonly TimeProvider _timeProvider;` field, populated via a constructor parameter.
- No `DateTime.UtcNow` or `DateTime.Now` call remains in any of the three handler files; all are replaced with `_timeProvider.GetUtcNow().DateTime`.
- No line outside the specified field/constructor additions and the exact replaced timestamp lines is changed in any of the three handler files.
- All three test files (`GetManufactureProtocolHandlerTests.cs`, `ResolveManualActionHandlerTests.cs`, `GetSemiproductRecipePdfHandlerTests.cs`) pass a `TimeProvider` (e.g. `TimeProvider.System`) to the handler constructor at every construction call site.
- No manual production `new XxxHandler(...)` call site outside DI/MediatR resolution and the three test files exists (confirmed by repo search); if one is found, it is updated too.
- `dotnet build` succeeds with no new warnings/errors introduced by this change.
- `dotnet test --filter FullyQualifiedName~Manufacture` passes, including all pre-existing tests in the three updated test files (timestamp assertions that only checked non-null/near-now remain valid and green).

**Verification:**
```bash
cd backend
dotnet build
dotnet test --filter FullyQualifiedName~Manufacture
```
Also run a targeted grep to confirm no stray `DateTime.UtcNow`/`DateTime.Now` remain in the three handler files:
```bash
grep -n "DateTime.UtcNow\|DateTime.Now" \
  src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol/GetManufactureProtocolHandler.cs \
  src/Anela.Heblo.Application/Features/Manufacture/UseCases/ResolveManualAction/ResolveManualActionHandler.cs \
  src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetSemiproductRecipePdf/GetSemiproductRecipePdfHandler.cs
```
(expected: no matches).
