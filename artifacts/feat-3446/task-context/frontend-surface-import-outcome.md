### task: frontend-surface-import-outcome

**Files:**
- Modify: `frontend/src/api/hooks/useBankStatements.ts:168-174`
- Modify: `frontend/src/components/customer/tabs/ImportTab.tsx:159-166`

This task extends the hand-written response types and branches the completion alert. It depends on `backend-surface-import-counts` producing the fields in the JSON response, but requires no generated-client change (`ImportTab.tsx` reads the hand-written `BankImportResponse`, not the generated client).

**Step 1 — extend the hook types.** In `useBankStatements.ts`, replace the two interfaces at lines 168–174:

```typescript
export interface BankStatementImportResult {
  statements: BankStatementImportDto[];
}

export interface BankImportResponse {
  statements: BankStatementImportDto[];
}
```
with:
```typescript
export interface BankStatementImportResult {
  statements: BankStatementImportDto[];
  successCount: number;
  errorCount: number;
  totalCount: number;
  hasErrors: boolean;
}

export interface BankImportResponse {
  statements: BankStatementImportDto[];
  successCount: number;
  errorCount: number;
  totalCount: number;
  hasErrors: boolean;
}
```

**Step 2 — branch the alert.** In `ImportTab.tsx`, `handleImportSubmit` (lines 159–166) currently discards the mutation result and shows a fixed alert. Replace:

```typescript
      // Single import request for the selected date
      await importMutation.mutateAsync({
        accountName: selectedAccount,
        dateFrom: importDate,
        dateTo: importDate,
      });

      // Show success message
      alert(`Import dokončen pro datum ${importDate}`);
```
with:
```typescript
      // Single import request for the selected date
      const result = await importMutation.mutateAsync({
        accountName: selectedAccount,
        dateFrom: importDate,
        dateTo: importDate,
      });

      // Show outcome message reflecting the per-run counts
      if (result.totalCount === 0) {
        alert(`Import dokončen pro datum ${importDate}: žádné výpisy k importu.`);
      } else if (result.hasErrors) {
        alert(
          `Import dokončen pro datum ${importDate}: ${result.successCount} úspěšně, ${result.errorCount} s chybou. Zkontrolujte seznam výpisů.`
        );
      } else {
        alert(`Import dokončen pro datum ${importDate}: ${result.successCount} výpisů úspěšně naimportováno.`);
      }
```

The surrounding lines (`refetch()`, `setShowImportModal(false)`, `resetImportForm()`, the `catch`/`finally` blocks) are unchanged.

**Step 3 — verify.** Run:
```
cd frontend && npm run build && npm run lint
```
Both must pass. Manually confirm the three branches read correctly: zero statements → "žádné výpisy k importu"; `hasErrors` true → both counts + "Zkontrolujte seznam výpisů"; otherwise → success count.

Commit message: `feat(bank): show import success/error counts in ImportTab completion message`
