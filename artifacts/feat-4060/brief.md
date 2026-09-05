## Module
Bank

## Finding
`frontend/src/components/customer/tabs/ImportTab.tsx` line 250 determines import success by comparing a raw string to the literal `"OK"`:

```tsx
const getImportStatusIcon = (importResult: string | undefined) => {
  if (importResult === "OK") {
```

Meanwhile, `backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportDto.cs` line 15 exposes an `errorType` computed property specifically to abstract this check:

```csharp
public string? ErrorType => ImportResult != ImportStatus.Success ? ImportResult : null;
```

`errorType` is `null` on success and carries the error string otherwise — exactly the distinction the frontend needs. It is generated into the TypeScript client by NSwag. The frontend ignores it entirely and re-derives the same information by hardcoding the backend's internal `ImportStatus.Success` constant (`"OK"`).

## Why it matters
- **Fragile coupling**: if `ImportStatus.Success` ever changes, the frontend breaks silently — no compile-time or type-system signal.
- **Dead code in the DTO**: `errorType` was added to prevent this exact coupling but is unused, so it carries complexity for zero benefit.
- **Duplication of domain knowledge** across the wire boundary — the DTO already encodes "is this an error" as a nullable string; the frontend re-encodes it with a magic literal.

## Suggested fix
In `ImportTab.tsx`, switch from checking `importResult` to checking `errorType`:

```tsx
const getImportStatusIcon = (importResult: string | undefined, errorType: string | null | undefined) => {
  if (!errorType) {   // null/undefined → success
    return <...success badge...>;
  }
  return <...error badge with errorType as the message...>;
};
```

And in the table row, pass `statement.errorType` alongside `statement.importResult`. This removes the `"OK"` literal from the frontend entirely. The `errorType` DTO property can then be justified by its consumer.

---
_Filed by daily arch-review routine on 2026-09-04._
