### task: move-marketing-import-result-to-contracts

**Goal:** Relocate `MarketingImportResult.cs` into the `MarketingInvoices` feature's `Contracts/` folder, update its namespace, and remove the now-dead `using` line in the handler test file. This single task covers spec requirements FR-1, FR-2, and FR-3. (FR-4 and FR-5 are verified by the build/test step in the next task — they require no code edits of their own, only confirmation nothing else changed.)

**Context:** See "Context for every task below" above for full background — this task is self-contained using that context; you do not need to re-derive anything, just perform the steps below exactly.

**Step 1 — Move the file with `git mv` (preserves history):**

From the repo root (`/home/user/worktrees/feature-4144-Arch-Review-Marketinginvoices-Marketingimportresul`), run:

```bash
git mv backend/src/Anela.Heblo.Application/Features/MarketingInvoices/MarketingImportResult.cs backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Contracts/MarketingImportResult.cs
```

**Verify:** Run `git status --short` and confirm it reports a renamed file, e.g. a line starting with `R  ` referencing both the old and new path. Also confirm with `ls backend/src/Anela.Heblo.Application/Features/MarketingInvoices/` that `MarketingImportResult.cs` is no longer listed there, and `ls backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Contracts/` now lists `MarketingImportResult.cs` alongside the existing `IMarketingTransactionSource.cs` and `MarketingTransaction.cs`.

**Step 2 — Update the namespace in the moved file:**

Open `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Contracts/MarketingImportResult.cs`. Its full current content (identical to before the move, since `git mv` does not alter file contents) is:

```csharp
namespace Anela.Heblo.Application.Features.MarketingInvoices;

public class MarketingImportResult
{
    public int Imported { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
}
```

Change only the first line, from:

```csharp
namespace Anela.Heblo.Application.Features.MarketingInvoices;
```

to:

```csharp
namespace Anela.Heblo.Application.Features.MarketingInvoices.Contracts;
```

The full file content after this edit must be exactly:

```csharp
namespace Anela.Heblo.Application.Features.MarketingInvoices.Contracts;

public class MarketingImportResult
{
    public int Imported { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
}
```

Do not change anything else in the file — the class name, property names, property types, and access modifiers stay byte-for-byte identical.

**Verify:** Run `cat backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Contracts/MarketingImportResult.cs` and confirm the output matches exactly the "full file content after this edit" block above.

**Step 3 — Remove the now-dead `using` line in the test file:**

Open `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/ImportMarketingInvoicesHandlerTests.cs`. Its first 9 lines currently read:

```csharp
using Anela.Heblo.Application.Features.MarketingInvoices;
using Anela.Heblo.Application.Features.MarketingInvoices.Contracts;
using Anela.Heblo.Application.Features.MarketingInvoices.Services;
using Anela.Heblo.Application.Features.MarketingInvoices.UseCases.ImportMarketingInvoices;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingInvoices;
```

Delete only the first line, `using Anela.Heblo.Application.Features.MarketingInvoices;`, so the top of the file becomes:

```csharp
using Anela.Heblo.Application.Features.MarketingInvoices.Contracts;
using Anela.Heblo.Application.Features.MarketingInvoices.Services;
using Anela.Heblo.Application.Features.MarketingInvoices.UseCases.ImportMarketingInvoices;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingInvoices;
```

Do not touch any other line in this file — in particular, leave the existing usage `new MarketingImportResult { Imported = 1, Skipped = 0, Failed = 0 }` further down in the file completely unchanged; it will resolve correctly via the remaining `using Anela.Heblo.Application.Features.MarketingInvoices.Contracts;` line.

**Verify:** Run `head -n 9 backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/ImportMarketingInvoicesHandlerTests.cs` and confirm the output matches exactly the 9-line block above (now starting with the `Contracts` using, no blank line inserted, no other change).

**Step 4 — Confirm no other files were touched:**

Run `git status --short` from the repo root. Confirm the only changes reported are:
- a rename/move entry for `MarketingImportResult.cs` (old path → new path under `Contracts/`)
- a modification to `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/ImportMarketingInvoicesHandlerTests.cs`

(There may also be a pre-existing unrelated modification to `artifacts/feat-4144/state.json` from earlier pipeline stages — that is expected and out of scope for this change; do not revert it.)

If any file other than these two appears modified, stop and re-check — this change must not touch `IMarketingInvoiceImportService.cs`, `MarketingInvoiceImportService.cs`, `ImportMarketingInvoicesHandler.cs`, `MarketingInvoiceImportServiceTests.cs`, `MarketingInvoicesModule.cs`, or any other `.cs` file.

---
