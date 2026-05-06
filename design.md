# Design: Parallelize Invoice Fetch in `ClassifyInvoices` Handler

## Component Design

### `ClassifyInvoicesHandler` (modified)

**File:** `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/ClassifyInvoices/ClassifyInvoicesHandler.cs`

The handler gains one private constant and one private type. Its `Handle` method replaces the sequential `foreach` fetch loop (lines 39–53) with a parallel fetch phase while leaving the classification loop (lines 65–105) and the batch-mode path (lines 56–61) byte-identical.

**Responsibilities after the change:**

| Phase | Mechanism | Concurrency |
|---|---|---|
| Specific-invoices fetch | `Task.WhenAll` + `SemaphoreSlim` throttle | Up to `MaxFetchConcurrency` concurrent Flexi calls |
| Result aggregation | Sequential traversal of `FetchOutcome[]` in input order | Single-threaded |
| Classification | Unchanged sequential `foreach` | Single-threaded |
| Batch fetch | Unchanged `GetUnclassifiedInvoicesAsync` | Single call |

**New private members:**

```csharp
private const int MaxFetchConcurrency = 8;

private readonly record struct FetchOutcome(
    string Id,
    ReceivedInvoiceDto? Invoice,
    string? FetchError);
```

`FetchOutcome` is defined as a `file`-scoped type (or a private nested type) in the same `.cs` file to keep it invisible outside the slice.

**Fetch-phase execution model:**

```
request.InvoiceIds = [A, B, C, …]
        │
        ▼
SemaphoreSlim(MaxFetchConcurrency, MaxFetchConcurrency) — using-scoped
        │
        ├─ Task<FetchOutcome> per id (materialised into List before WhenAll)
        │     throttle.WaitAsync(cancellationToken)
        │         → _invoicesClient.GetInvoiceByIdAsync(id)  [try/catch]
        │     throttle.Release()  [finally]
        │
        ▼
await Task.WhenAll(fetchTasks)  →  FetchOutcome[] in input order
        │
        ▼
Stopwatch stopped → LogDebug("Fetched {FetchedCount}/{RequestedCount} invoices in {ElapsedMs}ms")
        │
        ▼
Sequential aggregation pass (preserves FR-5 ordering invariant):
  Invoice != null              → append to invoicesToClassify
  Invoice == null, no error    → Errors++, errorMessages.Add("Invoice {id} not found"), LogWarning
  Invoice == null, FetchError  → Errors++, errorMessages.Add("Invoice {id}: fetch failed: {msg}"), LogError
        │
        ▼
[Unchanged classification loop]
```

**Cancellation boundary:** `cancellationToken` is forwarded only to `throttle.WaitAsync(cancellationToken)`. In-flight Flexi calls are not cancellable because `IReceivedInvoicesClient.GetInvoiceByIdAsync` does not accept a `CancellationToken` (out of scope).

**Logging contract:**

| Event | Level | Template |
|---|---|---|
| Fetch phase complete | Debug | `"Fetched {FetchedCount}/{RequestedCount} invoices in {ElapsedMs}ms"` |
| Invoice not found | Warning | `"Invoice {InvoiceId} not found"` (preserved verbatim) |
| Fetch exception | Error | `"Error fetching invoice {InvoiceId}"` |
| Classification start | Information | `"Starting classification of {Count} specific invoices"` (preserved verbatim) |

---

### Test class (new)

**File:** `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/ClassifyInvoicesHandlerTests.cs`

xUnit + FluentAssertions + Moq, hand-rolled fakes where Moq callback timing is insufficient (concurrency-limit test).

| Test method | FR covered | Key assertion |
|---|---|---|
| `Handle_FetchesInParallel_WhenMultipleIds` | FR-1, NFR-1 | 10 ids × 200 ms fake delay completes < 800 ms |
| `Handle_RespectsConcurrencyLimit` | FR-2 | Atomic in-flight counter never exceeds `MaxFetchConcurrency` during any window |
| `Handle_AppendsNotFoundError_AndContinues` | FR-3 | `Errors == 1`, `ErrorMessages` contains `"Invoice B not found"`, A and C reach the classifier |
| `Handle_FetchExceptionIsolatedToOneId` | FR-4 | `Errors == 1`, message contains id and exception text, remaining ids still classified |
| `Handle_PreservesInputOrderOfErrors` | FR-5 | `ErrorMessages` order matches `InvoiceIds` order across mixed missing/throwing ids |
| `Handle_BatchModeUnchanged` | FR-6 | `GetUnclassifiedInvoicesAsync` called exactly once; `GetInvoiceByIdAsync` never called |

---

## Data Schemas

### Unchanged public contracts

No schema changes. All of the following are read-only for this feature:

**`ClassifyInvoicesRequest`**
```csharp
public class ClassifyInvoicesRequest : IRequest<ClassifyInvoicesResponse>
{
    public List<string>? InvoiceIds { get; set; }   // null/empty → batch mode
    public bool ManualTrigger { get; set; } = false;
}
```

**`ClassifyInvoicesResponse`**
```csharp
public class ClassifyInvoicesResponse : BaseResponse
{
    public int TotalInvoicesProcessed { get; set; }
    public int SuccessfulClassifications { get; set; }
    public int ManualReviewRequired { get; set; }
    public int Errors { get; set; }
    public List<string> ErrorMessages { get; set; } = new();
}
```

**`IReceivedInvoicesClient`**
```csharp
public interface IReceivedInvoicesClient
{
    Task<List<ReceivedInvoiceDto>> GetUnclassifiedInvoicesAsync();
    Task<ReceivedInvoiceDto?> GetInvoiceByIdAsync(string invoiceId);  // no CancellationToken — out of scope
}
```

---

### New internal type

**`FetchOutcome`** — private to the handler file, never exposed via any API or interface:

```csharp
private readonly record struct FetchOutcome(
    string Id,              // original invoice id from request.InvoiceIds
    ReceivedInvoiceDto? Invoice,   // null on not-found or exception
    string? FetchError);    // null on success or not-found; exception message on caught exception
```

Discrimination table:

| `Invoice` | `FetchError` | Meaning |
|---|---|---|
| non-null | null | fetch succeeded |
| null | null | invoice not found (404-equivalent) |
| null | non-null | fetch threw an exception |

---

### Error message formats

Fetch-phase messages are appended to `response.ErrorMessages` **before** any classification-phase messages (FR-5 ordering invariant):

| Case | Format |
|---|---|
| Not found | `"Invoice {id} not found"` |
| Fetch exception | `"Invoice {id}: fetch failed: {exception.Message}"` |
| Classification error (existing) | `"Invoice {invoiceNumber}: {errorMessage}"` |
| Classification error with rule (existing) | `"Invoice {invoiceNumber} (Rule: {ruleName}): {errorMessage}"` |
