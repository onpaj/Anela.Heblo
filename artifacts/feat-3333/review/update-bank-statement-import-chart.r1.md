# Code Review: update-bank-statement-import-chart

## Summary
All three acceptance criteria are met: `BankStatementImportStatisticsDto` is gone, `parseISO` is fully removed, and `DailyBankStatementStatistics.date` is typed as `Date` in the generated client so direct `Date` usage is type-safe. One leftover `console.log` debug statement was introduced during development and should be removed before merging.

## Review Result: REVISION_NEEDED

### task: update-bank-statement-import-chart
**Status:** REVISION_NEEDED
**Issues:**
- **Debug `console.log` left in production code (line 58-62).** The block `console.log('Bank statements chart data:', ...)` was added during development and must be removed. It logs potentially sensitive business data (import counts, dates) to the browser console on every render.

## Overall Notes
The core migration is correct. `DailyBankStatementStatistics` carries `date?: Date` from the generated API client, so every `item.date!`, `d.date`, and `data.date` usage in the component is type-safe. The non-null assertions (`!`) are appropriate given that the existing callers already relied on the date always being present. Once the debug log is removed the file is ready to ship.

**Status:** REVISION_NEEDED
