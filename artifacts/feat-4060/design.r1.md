# Design: Bank ImportTab should use `errorType` instead of hardcoded "OK" sentinel

No UX/UI design work is required for this task. The rendered output (badge color, icon, Czech labels "Úspěch"/error text) is required to be pixel-identical before and after the change (spec FR-3; arch review Skip Design: true) — this is an internal refactor of which field drives an existing render branch, not a UI change. UX/UI sections are therefore omitted per this agent's own instructions.

## Component Design

### `getImportStatusIcon` (in `frontend/src/components/customer/tabs/ImportTab.tsx`)

**Responsibility:** Render the success/error status badge for one bank statement row.

**Contract (after this change):**

```tsx
getImportStatusIcon(errorType: string | null | undefined): JSX.Element
```

- Input: `errorType` — `null`/`undefined` means the import succeeded; any truthy string is the error to display.
- Output: unchanged JSX from today —
  - Falsy `errorType` → green pill, `CheckCircle` icon, text "Úspěch".
  - Truthy `errorType` → red pill, `AlertCircle` icon, text `errorType` (falls back to "Chyba" if `errorType` is an empty string).
- The `importResult` parameter is removed. It is no longer needed: the component now branches and derives its message entirely from `errorType`, which is `null` on success and equals `importResult` on error (guaranteed by the backend's `BankStatementImportDto.ErrorType` getter). No other responsibility of this function changes.

**Call site (single call site, table row rendering, currently line 494):**

```tsx
// before
{getImportStatusIcon(statement.importResult)}

// after
{getImportStatusIcon(statement.errorType)}
```

No other components are introduced, removed, or restructured. `ImportTab.tsx`'s surrounding table, sorting, loading/error states, and header are untouched.

## Data Schemas

No schema changes — `errorType` already exists on the wire and in the generated client, unmodified by this task:

```ts
// frontend/src/api/generated/api-client.ts (generated, already present, not modified by this task)
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

```csharp
// backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportDto.cs (unmodified by this task)
public string? ErrorType => ImportResult != ImportStatus.Success ? ImportResult : null;
```

This task consumes the existing `errorType` field; it defines no new fields, endpoints, or event payloads.
