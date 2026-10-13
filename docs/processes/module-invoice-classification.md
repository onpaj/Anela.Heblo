---
process: module-invoice-classification
kind: module
module: invoice-classification
summary: Rule-based pre-accounting of received supplier invoices — Heblo sets the Flexi accounting template (předkontace) and cost centre on invoices tagged KLASIFIKACE, and hands the rest to the accountant via the MANUAL-KLASIF label.
owns: []
verified_at: "5e993f9e2"
related:
  - feed-invoice-classification
---

# Invoice classification (Klasifikace faktur)

## Purpose
Every received supplier invoice (faktura přijatá) in Flexi needs an accounting template
(předkontace — Flexi `typUcOp`, evidence `predpis-zauctovani`) and usually a cost centre
(středisko) before it can be posted. For recurring suppliers this choice is always the same,
so the module lets staff keep a prioritised list of rules ("invoices from IČO X → template Y,
cost centre Z") and applies them automatically every hour. What no rule covers is flagged in
Flexi for the accountant. Heblo stores only the rules and an audit history; the invoices and
their classification live in Flexi.

## Users & screens
Page **Klasifikace faktur** — `/purchase/invoice-classification` (sidebar section Nákup),
feature permission `Purchase_InvoiceClassification`:
- Tab **Faktury** — classification history: date range, invoice-number and company filters,
  20 rows per page, result, rule, template, cost centre, error and who/when. Per row:
  *Klasifikovat* (re-run one invoice — broken, see quirks) and *Vytvořit pravidlo* (opens the
  rule form pre-filled with the company name as a *Název firmy* pattern).
- Tab **Pravidla** — active rules in evaluation order: create, edit (incl. the *Pravidlo je aktivní*
  checkbox), delete, drag & drop reorder. The form's template list comes live from Flexi; the cost-centre list from
  `GET /api/Departments` (Flexi střediska, shared with Finance).
- Button **Spustit klasifikaci** — runs the batch now and shows the counts in an alert.
- Button **Statistiky** — a modal with hard-coded mock numbers (see quirks).

No MCP tool exposes this module.

## Processes
- `feed-invoice-classification` — hourly Hangfire job `invoice-classification` (`0 * * * *`)
  plus the two manual buttons: evaluate rules on KLASIFIKACE-tagged Flexi invoices, write
  template + cost centre back or tag MANUAL-KLASIF, log ClassificationHistory.

Plain CRUD (no process doc), all under `api/InvoiceClassification`:
- Rules: `GET rules?includeInactive=`, `POST rules`, `PUT rules/{id}`, `DELETE rules/{id}`,
  `PUT rules/reorder`. A new rule gets `Order = max + 1` (bottom of the list). Reorder swaps the
  rows' existing `Order` values in two saves (via negative temporaries) because `Order` has a
  unique index across active and inactive rules.
- Lookups: `GET rule-types` (the five rule types), `GET accounting-templates` (Flexi templates
  with `modulFap = true`, code not starting `N-`, sorted by code), `GET history`,
  `GET invoice/{invoiceId}` (one Flexi invoice by document code; 404 if not found — not used by
  the current UI).

## Data owned
- `public."ClassificationRules"` — one rule: name, `RuleTypeIdentifier` (`ICO`,
  `COMPANY_NAME`, `DESCRIPTION`, `ITEM_DESCRIPTION`, `AMOUNT`), `Pattern`,
  `AccountingTemplateCode`, optional `Department` (cost-centre code), `Order` (unique),
  `IsActive`, created/updated by and at. Non-unique index on (`RuleTypeIdentifier`, `Pattern`).
- `public."ClassificationHistory"` — one classification attempt of one invoice: Flexi id
  (`AbraInvoiceId`) and number, invoice date, company, description, `Result`
  (1 Success / 2 ManualReviewRequired / 3 Error), rule id (FK, set NULL on rule delete),
  template, department, error, `Timestamp` (UTC), `ProcessedBy`. Append-only, never purged.

Created by migrations `20251031101657_InvoiceClassificationFeature` and
`20251031104021_FixInvoiceClassificationDateTimeHandling`.

## External systems
**Flexi (ABRA Flexi)**, via `Rem.FlexiBeeSDK.Client`:
- Read `faktura-prijata` — invoices by issue-date window + label, or by document code.
- Read `predpis-zauctovani` — accounting templates for the rule form.
- Write `faktura-prijata` — `typUcOp` (template) and `stredisko` (cost centre); labels
  (`stitky`) KLASIFIKACE and MANUAL-KLASIF.
- Read cost centres (`IDepartmentClient`, through the UserManagement `GetDepartments` use case).

How and when staff put the KLASIFIKACE label on invoices in Flexi is not defined in this repo.

## Dependencies
- Reads from UserManagement (`GetDepartments` → Flexi cost centres) for the rule form only.
- `IReceivedInvoicesClient` (domain interface of this module, Flexi implementation
  `FlexiReceivedInvoicesClient`) is also used by the marketing-performance ad-cost import
  (`FlexiMonthlyAdCostSource` → `SearchByVatIdsAsync`, received invoices by supplier DIČ and
  accounting date). That use is documented with the marketing module, not here.
- The nightly Flexi analytics sync (`sync-flexi-analytics`) separately copies all accounting
  templates into `flexi_raw` for Metabase; it does not read this module's tables.
- No other Heblo module reads `ClassificationRules` or `ClassificationHistory`.

## Known quirks
- **History *Klasifikovat* button does nothing useful** — it sends the Flexi internal id, the
  API looks up by document code, the SDK throws, and the API returns 200 with zero counts.
  Details in `feed-invoice-classification`.
- **History filter *Číslo faktury* matches the internal Flexi id**, not the invoice number.
- **Statistics modal is a mock**: `ClassificationStats.tsx` shows hard-coded numbers
  (150 / 120 / 25 / 5, 80 %) with a "TODO: Replace with actual API call"; no stats endpoint exists.
- **Rule edits take effect on the next run only** and never re-classify invoices already
  processed; an invoice classified wrongly must be fixed in Flexi (or re-tagged KLASIFIKACE
  if issued within the last 30 days).
- **Rule deletion is immediate and hard** — no soft delete; deactivate instead to keep the
  rule name visible in history.
- **A deactivated rule vanishes from the UI.** The page loads rules with
  `includeInactive=false`, so an unticked *Pravidlo je aktivní* rule is no longer listed and cannot be
  re-activated from the page (only via `GET rules?includeInactive=true` + `PUT rules/{id}`).
  It still holds its `Order` value.
- **Invoices older than 30 days (issue date) are invisible** to the job even when tagged.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/InvoiceClassificationModule.cs` — DI, registered rule types
- `backend/src/Anela.Heblo.API/Controllers/InvoiceClassificationController.cs` — all endpoints
- `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/ClassificationRule.cs` — rule entity
- `backend/src/Anela.Heblo.Persistence/InvoiceClassification/ClassificationRuleRepository.cs` — ordering and reorder
- `backend/src/Anela.Heblo.Persistence/InvoiceClassification/ClassificationRuleConfiguration.cs` — table, unique `Order`
- `frontend/src/pages/InvoiceClassification/InvoiceClassificationPage.tsx` — tabs, run button
- `frontend/src/pages/InvoiceClassification/ClassificationHistoryPage.tsx` — history tab
- `frontend/src/pages/InvoiceClassification/components/RuleForm.tsx` — rule form
- `frontend/src/api/hooks/useInvoiceClassification.ts` — API hooks
