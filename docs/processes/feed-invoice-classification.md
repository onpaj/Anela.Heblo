---
process: feed-invoice-classification
kind: feed
module: invoice-classification
summary: Hourly assigns an accounting template (předkontace) and optionally a cost centre (středisko) to Flexi received invoices tagged KLASIFIKACE, using Heblo's ordered classification rules; unmatched invoices are re-tagged MANUAL-KLASIF for the accountant, and every attempt is logged in ClassificationHistory.
owns:
  - backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Infrastructure/Jobs/InvoiceClassificationJob.cs
  - backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/ClassifyInvoices/**
  - backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Services/**
  - backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/Rules/**
  - backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/ClassificationHistory.cs
  - backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/ReceivedInvoice*.cs
  - backend/src/Anela.Heblo.Persistence/InvoiceClassification/ClassificationHistory*.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/InvoiceClassification/**
verified_at: "5e993f9e2"
related: []
---

# Received-invoice classification → Flexi

## Purpose
Saves the accountant from picking the accounting template (předkontace, Flexi field `typUcOp`)
and cost centre (středisko) by hand on every received supplier invoice (faktura přijatá).
Staff tag an invoice in Flexi with the label **KLASIFIKACE**; Heblo then tries its rules
(page *Klasifikace faktur*, `/purchase/invoice-classification`, tab *Pravidla*) top to bottom
and writes the template of the first matching rule back to the invoice in Flexi. When no rule
matches, Heblo swaps the label for **MANUAL-KLASIF**, so the accountant can filter those
invoices in Flexi and classify them by hand. Every attempt — success, manual review or error —
is a row in the history tab (*Faktury*) of the same page.

Heblo only sets the template and cost centre. Posting (zaúčtování) itself happens in Flexi.

## Trigger
- Hangfire recurring job **`invoice-classification`** (category Finance), cron `0 * * * *`
  (every hour on the hour, Europe/Prague), `DefaultIsEnabled = true`. Can be disabled or run
  manually in Recurring Jobs; a disabled job logs "is disabled" and exits.
- Button **Spustit klasifikaci** on the page → `POST /api/InvoiceClassification/classify`
  (body `{manualTrigger: true}`, no invoice ids) — the same batch run, synchronous, result shown
  in a browser alert.
- Button **Klasifikovat** on a history row → `POST /api/InvoiceClassification/classify/{invoiceId}`
  — classifies one invoice regardless of its labels (but see Known quirks: it is broken).

All endpoints require the `Purchase_InvoiceClassification` feature permission.

## Data flow
1. **Select invoices** (batch mode, `FlexiReceivedInvoicesClient.GetUnclassifiedInvoicesAsync`):
   one Flexi query on evidence `faktura-prijata` with filter
   `datVyst gte <today − DaysBack> and datVyst lte <today>` **and** `stitky eq "code:KLASIFIKACE"`,
   no paging (`limit=0`), ordered by `datVyst`. "Today" is the server's local time.
   Single-invoice mode instead looks the invoice up by `kod eq "<invoiceId>"`.
2. **Map** each Flexi invoice to `ReceivedInvoice`: `id` → `AbraInvoiceId`, `kod` →
   `InvoiceNumber`, `nazFirmy` → `CompanyName`, `ic` (IČO) → `CompanyVat`, `popis` →
   `Description`, `sumCelkem` → `TotalAmount`, `datVyst` → `InvoiceDate`, items
   `polozkyDokladu.nazev` → `Items[].Name`.
3. **Load rules**: all `public."ClassificationRules"` with `IsActive = true`, ordered by `Order`
   ascending (re-read for every invoice).
4. **Evaluate** (`RuleEvaluationEngine`): the first rule whose type evaluates true wins
   (see Logic). A rule whose `RuleTypeIdentifier` matches no registered type never matches.
5. **Write to Flexi**, per invoice, sequentially:
   - *Match* → `predpis-zauctovani` client `UpdateInvoiceAsync`: POST to `faktura-prijata`
     with `{"id":"code:<kod>","kod":<kod>,"typUcOp":"code:<rule template>","stredisko":"code:<rule department>"}`
     (`stredisko` omitted when the rule has no department). On success, remove both labels
     KLASIFIKACE and MANUAL-KLASIF from the invoice.
   - *No match* → add label MANUAL-KLASIF, then remove KLASIFIKACE.
   Labels are changed read-modify-write: read `stitky`, edit the list, save the whole list with
   `stitky@removeAll = true`.
6. **Log** one row in `public."ClassificationHistory"` per attempt: invoice id/number/date,
   company, description, `Result` (1 Success, 2 ManualReviewRequired, 3 Error), matched rule id,
   template, department, error message, `Timestamp` (UTC, stored without zone), `ProcessedBy`
   (user name for API calls, `system` for the Hangfire job).
7. **Report**: counts of success / manual review / errors are returned to the caller; the job
   also logs them and sends App Insights business event `InvoiceClassification`
   (`Status` = Success when there were no errors, otherwise PartialSuccess).

## Logic & formulas
Rule types (`RuleTypeIdentifier` → what is compared with `Pattern`); all text matches are
case-insensitive:

| Id | UI name | Compares | Match rule |
|---|---|---|---|
| `ICO` | IČO | supplier IČO (Flexi `ic`) | trimmed exact equality |
| `COMPANY_NAME` | Název firmy | supplier name (`nazFirmy`) | .NET regex; if the pattern is not a valid regex, plain "contains" |
| `DESCRIPTION` | Popis faktury | invoice description (`popis`) | same as above |
| `ITEM_DESCRIPTION` | Popis položky | name of any invoice line (`polozkyDokladu.nazev`) | same as above, true if any line matches |
| `AMOUNT` | Částka | invoice total **with VAT in CZK** (`sumCelkem`) | pattern `>=N`, `<=N`, `>N`, `<N`, `=N` or bare `N` (equality); unparseable → no match |

- Regex is unanchored (`Regex.IsMatch`), so `ABC` matches anywhere in the text; use `^…$`
  for an exact name. Empty text or empty pattern never matches.
- Only one condition per rule — there is no AND/OR between rule types. Order (drag & drop in
  the Rules tab, `PUT /rules/reorder`) is therefore the only way to express priority: put
  narrow rules (e.g. one supplier + amount) above broad ones.
- `AMOUNT` numbers are parsed with the server's current culture (`decimal.TryParse` without a
  culture), and compare against the CZK total even for foreign-currency invoices.
- A match does **not** check that the invoice already has a template — single-invoice mode
  overwrites whatever is on the invoice.

Outcomes per invoice:

| Situation | Flexi write | History `Result` |
|---|---|---|
| Rule matched, Flexi update OK | template + cost centre set, both labels removed | Success |
| Rule matched, Flexi update returned failure | nothing (labels stay) | Error, "Failed to update invoice classification in ABRA" |
| No rule matched | MANUAL-KLASIF added, KLASIFIKACE removed | ManualReviewRequired |
| Exception (Flexi down, label write failed, …) | whatever happened before the exception | Error, "Exception during classification: …", no rule id |

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `DataSourceOptions:InvoiceClassificationDaysBack` | 30 (class default; 90 in `appsettings.Development.json`) | Batch window on issue date `datVyst` |
| `DataSourceOptions:InvoiceClassificationTriggerLabel` | `KLASIFIKACE` | Flexi label (štítek) that queues an invoice |
| `DataSourceOptions:InvoiceClassificationManualReviewLabel` | `MANUAL-KLASIF` | Flexi label set when no rule matches |
| Recurring job `invoice-classification` | `0 * * * *`, enabled | Hourly batch run |

Flexi connection settings are shared with the rest of the Flexi adapter.

## Runtime facts
None.

## Known quirks
- **The history row's *Klasifikovat* button cannot work** (read from code, not observed). The
  history DTO's `invoiceId` is the Flexi internal numeric `id` (`AbraInvoiceId`), but the
  endpoint looks the invoice up by document code (`kod eq "<id>"`). The SDK then throws
  `KeyNotFoundException` instead of returning null, so the handler's "not found" branch is dead:
  the outer catch returns HTTP 200 with all counters 0 and only an
  "Classification process error: …" message, which the page ignores. Nothing is classified and
  no history row is written. Single-invoice classification works only if called with the
  document code (e.g. via API).
- **The history filter *Číslo faktury* searches the internal Flexi id**, not the invoice number:
  `GetPagedHistoryAsync` filters `AbraInvoiceId.Contains(...)`. Typing a document number
  (e.g. `FP-…`) finds nothing.
- **Invoices issued more than `InvoiceClassificationDaysBack` (30) days ago are never picked
  up**, even with the KLASIFIKACE label — the window is on issue date, not on when the label was
  set. Classify those by hand in Flexi.
- **A failed write repeats every hour.** If the template update fails (Error) the KLASIFIKACE
  label stays, so the next hourly run tries again and adds another history row. Same if the
  label removal after a successful update throws: the template is already set in Flexi, but the
  history says Error with no rule, and the invoice is reprocessed next hour.
- **Manual-review label write is not checked.** `MarkInvoiceForManualReviewAsync`'s boolean
  result is ignored and the ManualReviewRequired history row is written before the label
  change. If the label save fails without throwing, the invoice keeps KLASIFIKACE and is retried
  hourly, adding a ManualReviewRequired row each time.
- **The rule's template and cost centre are not validated at classification time.** The rule
  form offers only templates with `modulFap = true` whose code does not start with `N-`
  (`GetValidAccountingTemplatesAsync`), but a template later disabled or deleted in Flexi makes
  every matching invoice end in Error.
- **No overlap guard.** The job has no `DisableConcurrentExecution`; a manual
  *Spustit klasifikaci* during the hourly run processes the same invoices twice (same result,
  duplicate history rows and Flexi writes).
- **`ManualTrigger` is ignored** — set by the API (true) and the job (false) but never read.
- **Regex patterns run without a timeout**; a pathological user regex could stall a run.
- **Rule type `ICO`'s class is called `VatClassificationRule`** and its domain field
  `CompanyVat`, but it compares the IČO (`ic`), not the DIČ. The DIČ is in `SupplierVatId` and
  is not used by any rule.
- **Deleting a rule** sets `ClassificationRuleId` in its history rows to NULL (FK
  `ON DELETE SET NULL`), so old rows lose the rule name; the template code stays.
- Error texts say "ABRA" — that is Flexi (ABRA Flexi), not a separate system.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Infrastructure/Jobs/InvoiceClassificationJob.cs` — job id, cron, telemetry
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/ClassifyInvoices/ClassifyInvoicesHandler.cs` — batch vs single mode, counters
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Services/InvoiceClassificationService.cs` — per-invoice outcome and history row
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Services/RuleEvaluationEngine.cs` — first-match evaluation
- `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/Rules/` — the five rule types
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/InvoiceClassification/FlexiReceivedInvoicesClient.cs` — invoice query (date window + label)
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/InvoiceClassification/FlexiInvoiceClassificationsClient.cs` — template/cost-centre write, label changes, template list
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/InvoiceClassification/FlexiReceivedInvoiceMappingProfile.cs` — Flexi field mapping
- `backend/src/Anela.Heblo.Persistence/InvoiceClassification/ClassificationHistoryRepository.cs` — history paging and filters
- `backend/src/Anela.Heblo.Application/Common/DataSourceOptions.cs` — labels and days-back defaults
- Flexi SDK (`Rem.FlexiBeeSDK.Client` 0.1.143, repo `onpaj/FlexiBeeSDK`): `AccountingTemplateClient.UpdateInvoiceAsync`, `ReceivedInvoiceClient.AddTagAsync/RemoveTagAsync`, `ReceivedInvoiceRequest`
