## Module
InvoiceClassification

## Finding
`IClassificationHistoryRepository` declares two methods that no handler ever calls:

- `GetHistoryAsync(int skip, int take)` — `IClassificationHistoryRepository.cs:7`, implemented in `ClassificationHistoryRepository.cs:22`
- `GetHistoryByInvoiceIdAsync(string abraInvoiceId)` — `IClassificationHistoryRepository.cs:9`, implemented in `ClassificationHistoryRepository.cs:32`

The only handler that uses this repository is `GetClassificationHistoryHandler`, which calls only `GetPagedHistoryAsync`. A codebase-wide search confirms neither of the two methods is called from any handler, service, or test.

## Why it matters
Dead methods on a domain interface violate YAGNI and the Interface Segregation Principle: callers are forced to stub/mock additional surface they do not consume. The `GetHistoryByInvoiceIdAsync` method also conflates `AbraInvoiceId` with `InvoiceNumber` in a way that could mislead a future implementer (see companion issue). Keeping dead methods on the contract makes it harder to reason about what the module actually requires.

## Suggested fix
Remove `GetHistoryAsync` and `GetHistoryByInvoiceIdAsync` from `IClassificationHistoryRepository` and delete their implementations in `ClassificationHistoryRepository`. If a future use-case needs them, add them back when the consumer exists.

---
_Filed by daily arch-review routine on 2026-07-07._
