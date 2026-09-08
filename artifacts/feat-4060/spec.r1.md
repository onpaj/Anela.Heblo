# Specification: Bank ImportTab should use `errorType` instead of hardcoded "OK" sentinel

## Summary
`ImportTab.tsx` currently derives bank-statement import success by comparing the raw `importResult` string to the literal `"OK"`, silently duplicating the backend's internal `ImportStatus.Success` constant. The `BankStatementImportDto` already exposes an `errorType` field (`null` on success, the error string otherwise) generated into the TypeScript client, specifically to avoid this coupling. This change switches the frontend status check to use `errorType`, removing the `"OK"` magic literal entirely.

## Background
`backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportDto.cs` defines:

```csharp
public string? ErrorType => ImportResult != ImportStatus.Success ? ImportResult : null;
```

This computed property is generated into `frontend/src/api/generated/api-client.ts` (`BankStatementImportDto.errorType?: string | undefined`) via NSwag and is already present on every statement object returned by the bank-statements list endpoint. `frontend/src/components/customer/tabs/ImportTab.tsx` ignores this field and instead re-derives the same success/error distinction by comparing `importResult` to the literal `"OK"` (line 250), which is `ImportStatus.Success`'s current string value on the backend. If that backend constant ever changes, the frontend check silently breaks with no compile-time signal, and `errorType` remains dead code carrying complexity for no consumer.

## Functional Requirements

### FR-1: `getImportStatusIcon` determines success via `errorType`, not the `"OK"` literal
Replace the `importResult === "OK"` check with an `errorType` nullness check. `errorType` is `null`/`undefined` on success and holds the error string otherwise.

**Acceptance criteria:**
- `getImportStatusIcon` accepts a second parameter, `errorType: string | null | undefined`.
- The success branch renders when `errorType` is falsy (`null`, `undefined`, or `""`).
- The error branch renders when `errorType` is truthy, and displays `errorType` as the error message (replacing the current `importResult || "Chyba"` fallback text).
- The literal string `"OK"` no longer appears anywhere in `ImportTab.tsx`'s status-determination logic.

### FR-2: Call site passes `errorType` through
Update the call at line 494 (`{getImportStatusIcon(statement.importResult)}`) to also pass `statement.errorType`.

**Acceptance criteria:**
- The call becomes `getImportStatusIcon(statement.importResult, statement.errorType)` (or equivalent — see Open Questions on whether `importResult` is still needed as a parameter).
- No other call sites of `getImportStatusIcon` are missed (repo-wide search confirms line 494 is the only call site as of this spec).

### FR-3: Behavior is unchanged for existing data
For any statement where `ImportStatus.Success` is `"OK"` (current backend state), the rendered UI (badge color, icon, Czech label "Úspěch", error text) must be pixel-identical before and after this change — this is a refactor of *how* success is determined, not a UI/behavior change.

**Acceptance criteria:**
- A statement with `importResult === "OK"` and `errorType === null` still renders the green "Úspěch" badge with `CheckCircle`.
- A statement with a non-"OK" `importResult` and a non-null `errorType` still renders the red badge with `AlertCircle` and the error text.

## Non-Functional Requirements

### NFR-1: Type safety
The new `errorType` parameter must be typed to match the generated client (`string | undefined` per `BankStatementImportDto.errorType`, or `string | null | undefined` to be defensive per the issue's suggested signature — see Open Questions). No `any` types introduced.

### NFR-2: No backend changes
This is a frontend-only change. `BankStatementImportDto.cs` and `ImportStatus` are not modified — `errorType` already exists and is already generated into the TS client; this task only wires an existing, unused field into an existing consumer.

## Data Model
No data model changes. Relevant existing shape (`frontend/src/api/generated/api-client.ts`, generated from `BankStatementImportDto.cs`, do not hand-edit — see Dependencies):

```ts
export interface IBankStatementImportDto {
    id?: number;
    transferId?: string;
    statementDate?: Date;
    importDate?: Date;
    account?: string;
    currency?: string;
    itemCount?: number;
    importResult?: string;
    errorType?: string | undefined;
}
```

## API / Interface Design
No new endpoints. Existing bank-statements list response already carries `errorType` per row; this task only changes how `ImportTab.tsx` consumes a field it already receives.

Affected function signature (`frontend/src/components/customer/tabs/ImportTab.tsx`):

```tsx
// Before
const getImportStatusIcon = (importResult: string | undefined) => { ... }
// call site: getImportStatusIcon(statement.importResult)

// After
const getImportStatusIcon = (importResult: string | undefined, errorType: string | undefined) => { ... }
// call site: getImportStatusIcon(statement.importResult, statement.errorType)
```

`importResult` is retained as a parameter only if still used for display/logging inside the function after the switch (see FR-1); otherwise it may be dropped — left to the implementer per Open Questions, but the acceptance criteria in FR-1/FR-2 hold regardless.

## Dependencies
- `frontend/src/api/generated/api-client.ts` — auto-generated by NSwag on backend build (per `docs/development/api-client-generation.md`). `errorType` is already present in this file; no regeneration is required for this task, but any regeneration triggered by unrelated backend changes must not remove it.
- No other feature/module dependencies. This is isolated to the Bank module's `ImportTab.tsx`.

## Out of Scope
- Any change to `BankStatementImportDto.cs` or `ImportStatus` on the backend.
- Any change to other hardcoded status strings elsewhere in the Bank module (only `ImportTab.tsx`'s `getImportStatusIcon` is in scope, per the issue).
- Localization/wording changes to "Úspěch" / "Chyba" beyond what's needed to keep current behavior (FR-3).
- Adding tests infrastructure if none exists for this component today; if existing tests cover `getImportStatusIcon`/`ImportTab`, they must be updated to keep passing, not newly authored wholesale.

## Open Questions
None.
