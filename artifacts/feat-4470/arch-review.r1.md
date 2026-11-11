# Architecture Review: Type CreatePurchaseOrderRequest date fields as DateTime

## Skip Design: true
Contract/type change in backend with a minimal mapping adjustment in an existing frontend helper; no new UI components or visual decisions.

## Architectural Fit Assessment
Aligns with `docs/architecture/development_guidelines.md` (DTOs are classes, validation in FluentValidation, thin handlers). Mirrors the already-correct `UpdatePurchaseOrderRequest`. Integration points: `PurchaseOrdersController`, `CreatePurchaseOrderHandler`, `CreatePurchaseOrderRequestValidator`, generated TS client, `PurchaseOrderHelpers.tsx`.

## Proposed Architecture

### Component Overview
Request (typed DateTime) -> model binding (400 on malformed) -> FluentValidation (typed rules) -> Handler (normalizes to UTC date) -> `PurchaseOrder` ctor (Kind normalization, unchanged).

### Key Design Decisions

#### Decision 1: Where to normalize to UTC
**Options considered:** (a) rely on `PurchaseOrder` ctor only; (b) normalize in handler.
**Chosen approach:** (b) in handler: `DateTime.SpecifyKind(request.OrderDate.Date, DateTimeKind.Utc)` and the nullable equivalent.
**Rationale:** Preserves existing date-only truncation (old code used `.Date`). Ctor's `ToUniversalTime()` on a Local-kind value would shift the calendar day; truncating with SpecifyKind first avoids that. Also the order-number generator receives a stable UTC date.

#### Decision 2: Keep or delete string DateExtensions
**Chosen approach:** Grep for remaining usages; remove the string overloads only if they become unused (they are only used by this handler per grep); leave the `DateOnly` overload.
**Rationale:** Surgical change; avoid dead code but do not touch unrelated helpers.

#### Decision 3: Missing OrderDate
**Chosen approach:** `[Required]` is ineffective on a non-nullable `DateTime`; validator rule `NotEqual(default(DateTime))` with message "Order date is required".
**Rationale:** Prevents silent `0001-01-01` binding.

## Implementation Guidance

### Directory / Module Structure
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/CreatePurchaseOrder/CreatePurchaseOrderRequest.cs`
- `.../CreatePurchaseOrderHandler.cs`
- `.../CreatePurchaseOrderRequestValidator.cs`
- `backend/src/Anela.Heblo.Application/Common/Extensions/DateExtensions.cs` (only if string helpers become unused)
- Tests: `backend/test/Anela.Heblo.Tests/Features/Purchase/CreatePurchaseOrderHandlerTests.cs`, `CreatePurchaseOrderRequestValidatorTests.cs`, `Controllers/PurchaseOrdersControllerTests.cs`
- `frontend/src/components/purchase-orders/form/PurchaseOrderHelpers.tsx` (and regenerated `frontend/src/api/generated/api-client.ts` via build)

### Interfaces and Contracts
```csharp
public DateTime OrderDate { get; set; }
public DateTime? ExpectedDeliveryDate { get; set; }
```

### Data Flow
JSON `"2026-10-23"` or full ISO -> `DateTime` (Kind Unspecified or Utc/Local) -> validator -> handler `.Date` + SpecifyKind(Utc) -> entity.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| FE sends `Date` serialized via `toISOString()`, shifting calendar day for local-midnight dates | Medium | Construct the Date as UTC midnight from the `YYYY-MM-DD` form value (`new Date(value + "T00:00:00Z")`) in the helper; add FE test |
| Handler receives Local-kind DateTime with time component | Low | Handler takes `.Date` then SpecifyKind Utc, as old code did |
| Other callers (MCP tools, tests) construct the request with strings | Medium | Grep all `CreatePurchaseOrderRequest` usages incl. MCP; fix compile errors |
| Generated client regeneration noise | Low | Commit only expected diff |

## Specification Amendments
None.

## Prerequisites
None (no migrations, no config).
