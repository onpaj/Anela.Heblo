# Parallelize Invoice Fetch in `ClassifyInvoices` Handler — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the sequential per-id `GetInvoiceByIdAsync` loop in `ClassifyInvoicesHandler` with a bounded-concurrency parallel fetch so that classifying N specific invoices is roughly `(N / MaxConcurrency)` Flexi round-trips long instead of `N`, while preserving the existing public contract and tightening per-id fetch error reporting.

**Architecture:** A single MediatR handler edit. The fetch phase becomes `Task.WhenAll(ids.Select(...))` over a `SemaphoreSlim`-throttled lambda; each task returns an immutable `FetchOutcome` record struct (`Id`, `Invoice?`, `FetchError?`); a sequential aggregation pass walks the result array in input order to build `invoicesToClassify`/`errorMessages`. The classification loop and the batch-mode path remain byte-identical. New tests live in a new xUnit + FluentAssertions + Moq test class.

**Tech Stack:** .NET 8, C# 12, MediatR, xUnit, FluentAssertions, Moq, `SemaphoreSlim`, `Task.WhenAll`, `System.Diagnostics.Stopwatch`. No new packages.

---

## File Structure

| Path | Operation | Responsibility |
|------|-----------|----------------|
| `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/ClassifyInvoices/ClassifyInvoicesHandler.cs` | Modify | Replace lines 36–55 (sequential fetch loop) with throttled parallel fetch + sequential aggregation; add `using` directives, `MaxFetchConcurrency` constant, and `FetchOutcome` record struct. |
| `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/ClassifyInvoicesHandlerTests.cs` | Create | xUnit test class with 6 tests covering FR-1..FR-6 (parallelism, concurrency limit, not-found error, fetch exception isolation, input-order errors, batch mode unchanged). |

No other files are touched. No interfaces, DTOs, controllers, or DI registrations change.

**Why this decomposition:** All behavior lives inside the slice (`Anela.Heblo.Application/Features/InvoiceClassification/UseCases/ClassifyInvoices/`). The new `FetchOutcome` is private to the handler file (file-scoped or nested). The test class mirrors `ClassificationHistoryRepositoryTests.cs` for project-style consistency.

---

## Project Conventions to Follow

These are non-obvious gotchas the engineer must respect (verified from `CLAUDE.md` + the project tree):

- **Tests use xUnit + FluentAssertions + Moq.** The `Anela.Heblo.Tests` csproj already references all three; `using Xunit;` is provided globally via `<Using Include="Xunit" />`. Add `using FluentAssertions;` and `using Moq;` per-file.
- **DTOs are classes, never records.** This applies only to types crossing the OpenAPI boundary; `FetchOutcome` is purely internal (private record struct), so the rule does not apply to it.
- **Validation:** before declaring done, run `dotnet build` + `dotnet format` for backend and ensure all touched tests pass.
- **`dotnet test`** is invoked at solution root or against the test project: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`.
- **Surgical changes:** the spec mandates that line 56–61 (batch mode) and lines 65–105 (classification loop) stay byte-identical. Do not refactor surrounding code.
- **Ordering invariant (FR-5):** all fetch-phase errors must precede classification-phase errors in `errorMessages`. The aggregation pass runs before the classification loop, so this falls out naturally — but a code comment must call it out so a future refactor does not re-order for "efficiency".
- **Behavior change to document in the PR:** today, a single Flexi `GetInvoiceByIdAsync` exception aborts the whole request. After this change, it is reported per-id and the rest still classify. Mention this explicitly in the PR description.

---

## Task 1: Add the failing parallelism test

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/ClassifyInvoicesHandlerTests.cs`

This task seeds the test class skeleton plus the first behavioral test (FR-1 / NFR-1 — parallel fetch beats sequential by an order of magnitude). Subsequent tasks add more tests to the same file.

- [ ] **Step 1: Create the test class with helpers and the parallelism test**

Create the file with the following exact content:

```csharp
using System.Collections.Concurrent;
using System.Diagnostics;
using Anela.Heblo.Application.Features.InvoiceClassification.Services;
using Anela.Heblo.Application.Features.InvoiceClassification.UseCases.ClassifyInvoices;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Anela.Heblo.Tests.Features.InvoiceClassification;

public class ClassifyInvoicesHandlerTests
{
    private static InvoiceClassificationResult SuccessResult() => new()
    {
        Result = ClassificationResult.Success
    };

    private static ReceivedInvoiceDto Invoice(string id) => new()
    {
        InvoiceNumber = id,
        CompanyName = $"Company-{id}",
        TotalAmount = 100m,
        Description = "test"
    };

    private static ClassifyInvoicesHandler BuildHandler(
        IReceivedInvoicesClient invoicesClient,
        IInvoiceClassificationService? classificationService = null,
        IClassificationRuleRepository? ruleRepository = null)
    {
        classificationService ??= Mock.Of<IInvoiceClassificationService>(s =>
            s.ClassifyInvoiceAsync(It.IsAny<ReceivedInvoiceDto>()) ==
            Task.FromResult(SuccessResult()));

        ruleRepository ??= Mock.Of<IClassificationRuleRepository>();

        return new ClassifyInvoicesHandler(
            invoicesClient,
            classificationService,
            ruleRepository,
            NullLogger<ClassifyInvoicesHandler>.Instance);
    }

    [Fact]
    public async Task Handle_FetchesInParallel_WhenMultipleIds()
    {
        // Arrange: 10 ids, each fake fetch sleeps 200 ms.
        // Sequential would be ~2000 ms; with concurrency >= 5 we expect well under 800 ms.
        var ids = Enumerable.Range(1, 10).Select(i => $"INV-{i}").ToList();

        var clientMock = new Mock<IReceivedInvoicesClient>();
        clientMock
            .Setup(c => c.GetInvoiceByIdAsync(It.IsAny<string>()))
            .Returns<string>(async id =>
            {
                await Task.Delay(200);
                return Invoice(id);
            });

        var handler = BuildHandler(clientMock.Object);
        var request = new ClassifyInvoicesRequest { InvoiceIds = ids };

        // Act
        var sw = Stopwatch.StartNew();
        var response = await handler.Handle(request, CancellationToken.None);
        sw.Stop();

        // Assert
        response.TotalInvoicesProcessed.Should().Be(10);
        response.Errors.Should().Be(0);
        sw.ElapsedMilliseconds.Should().BeLessThan(800,
            "10 fetches at 200ms each should fan out under the throttle, not run sequentially");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails for the right reason**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ClassifyInvoicesHandlerTests.Handle_FetchesInParallel_WhenMultipleIds"`

Expected: **FAIL** with `Expected sw.ElapsedMilliseconds to be less than 800, but found ~2000`. (The current implementation is sequential.)

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/ClassifyInvoicesHandlerTests.cs
git commit -m "test: add failing parallel-fetch test for ClassifyInvoicesHandler"
```

---

## Task 2: Implement parallel fetch in the handler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/ClassifyInvoices/ClassifyInvoicesHandler.cs`

Replace the sequential fetch loop (lines 36–55 of the current file) with a throttled parallel fetch. Keep lines 56–61 (batch mode) and 65–105 (classification loop) byte-identical.

- [ ] **Step 1: Apply the handler edit**

Replace the entire content of `ClassifyInvoicesHandler.cs` with:

```csharp
using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using Anela.Heblo.Application.Features.InvoiceClassification.Services;

namespace Anela.Heblo.Application.Features.InvoiceClassification.UseCases.ClassifyInvoices;

public class ClassifyInvoicesHandler : IRequestHandler<ClassifyInvoicesRequest, ClassifyInvoicesResponse>
{
    // Conservative cap on concurrent Flexi fetches. Promote to IOptions if per-environment tuning is needed.
    private const int MaxFetchConcurrency = 8;

    private readonly IReceivedInvoicesClient _invoicesClient;
    private readonly IInvoiceClassificationService _classificationService;
    private readonly IClassificationRuleRepository _ruleRepository;
    private readonly ILogger<ClassifyInvoicesHandler> _logger;

    public ClassifyInvoicesHandler(
        IReceivedInvoicesClient invoicesClient,
        IInvoiceClassificationService classificationService,
        IClassificationRuleRepository ruleRepository,
        ILogger<ClassifyInvoicesHandler> logger)
    {
        _invoicesClient = invoicesClient;
        _classificationService = classificationService;
        _ruleRepository = ruleRepository;
        _logger = logger;
    }

    public async Task<ClassifyInvoicesResponse> Handle(ClassifyInvoicesRequest request, CancellationToken cancellationToken)
    {
        var response = new ClassifyInvoicesResponse();
        var errorMessages = new List<string>();

        try
        {
            List<ReceivedInvoiceDto> invoicesToClassify;

            if (request.InvoiceIds != null && request.InvoiceIds.Count > 0)
            {
                // Specific invoices mode — fetch in parallel under a SemaphoreSlim throttle.
                // The cancellationToken flows only into throttle.WaitAsync; in-flight Flexi calls
                // are not cancellable because IReceivedInvoicesClient.GetInvoiceByIdAsync has no token overload.
                using var throttle = new SemaphoreSlim(MaxFetchConcurrency, MaxFetchConcurrency);
                var sw = Stopwatch.StartNew();

                var fetchTasks = request.InvoiceIds.Select(id => FetchOneAsync(id, throttle, cancellationToken)).ToList();
                var fetchResults = await Task.WhenAll(fetchTasks);

                sw.Stop();
                _logger.LogDebug(
                    "Fetched {FetchedCount}/{RequestedCount} invoices in {ElapsedMs}ms",
                    fetchResults.Count(r => r.Invoice != null),
                    request.InvoiceIds.Count,
                    sw.ElapsedMilliseconds);

                // Sequential aggregation in input order — preserves FR-5 ordering invariant.
                // Do NOT reorder this loop for "efficiency"; downstream expects input-order errors.
                invoicesToClassify = new List<ReceivedInvoiceDto>(fetchResults.Length);
                foreach (var r in fetchResults)
                {
                    if (r.Invoice != null)
                    {
                        invoicesToClassify.Add(r.Invoice);
                    }
                    else if (r.FetchError != null)
                    {
                        response.Errors++;
                        errorMessages.Add($"Invoice {r.Id}: fetch failed: {r.FetchError}");
                    }
                    else
                    {
                        response.Errors++;
                        errorMessages.Add($"Invoice {r.Id} not found");
                        _logger.LogWarning("Invoice {InvoiceId} not found", r.Id);
                    }
                }

                _logger.LogInformation("Starting classification of {Count} specific invoices", invoicesToClassify.Count);
            }
            else
            {
                // Batch mode - all unclassified invoices
                invoicesToClassify = await _invoicesClient.GetUnclassifiedInvoicesAsync();
                _logger.LogInformation("Starting classification of {Count} unclassified invoices", invoicesToClassify.Count);
            }

            response.TotalInvoicesProcessed = invoicesToClassify.Count;

            foreach (var invoice in invoicesToClassify)
            {
                try
                {
                    var result = await _classificationService.ClassifyInvoiceAsync(invoice);

                    switch (result.Result)
                    {
                        case ClassificationResult.Success:
                            response.SuccessfulClassifications++;
                            break;
                        case ClassificationResult.ManualReviewRequired:
                            response.ManualReviewRequired++;
                            break;
                        case ClassificationResult.Error:
                            response.Errors++;
                            if (!string.IsNullOrEmpty(result.ErrorMessage))
                            {
                                // Add rule name to error message if available
                                var errorMessage = $"Invoice {invoice.InvoiceNumber}: {result.ErrorMessage}";
                                if (result.RuleId.HasValue)
                                {
                                    var rule = await _ruleRepository.GetByIdAsync(result.RuleId.Value);
                                    if (rule != null)
                                    {
                                        errorMessage = $"Invoice {invoice.InvoiceNumber} (Rule: {rule.Name}): {result.ErrorMessage}";
                                    }
                                }
                                errorMessages.Add(errorMessage);
                            }
                            break;
                    }
                }
                catch (Exception ex)
                {
                    response.Errors++;
                    var errorMessage = $"Invoice {invoice.InvoiceNumber}: {ex.Message}";
                    errorMessages.Add(errorMessage);
                    _logger.LogError(ex, "Error classifying invoice {InvoiceId}", invoice.InvoiceNumber);
                }
            }

            response.ErrorMessages = errorMessages;

            _logger.LogInformation("Classification completed. Success: {Success}, Manual Review: {ManualReview}, Errors: {Errors}",
                response.SuccessfulClassifications, response.ManualReviewRequired, response.Errors);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during invoice classification process");
            response.ErrorMessages.Add($"Classification process error: {ex.Message}");
        }

        return response;
    }

    private async Task<FetchOutcome> FetchOneAsync(string id, SemaphoreSlim throttle, CancellationToken cancellationToken)
    {
        await throttle.WaitAsync(cancellationToken);
        try
        {
            try
            {
                var invoice = await _invoicesClient.GetInvoiceByIdAsync(id);
                return new FetchOutcome(id, invoice, FetchError: null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching invoice {InvoiceId}", id);
                return new FetchOutcome(id, Invoice: null, FetchError: ex.Message);
            }
        }
        finally
        {
            throttle.Release();
        }
    }

    private readonly record struct FetchOutcome(string Id, ReceivedInvoiceDto? Invoice, string? FetchError);
}
```

- [ ] **Step 2: Build the solution**

Run: `dotnet build backend/Anela.Heblo.sln`

Expected: **Build succeeded. 0 Warning(s). 0 Error(s).** If new warnings appear (especially nullable warnings on `FetchOutcome`), fix at the source — do not suppress.

- [ ] **Step 3: Re-run the parallelism test — it must pass now**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ClassifyInvoicesHandlerTests.Handle_FetchesInParallel_WhenMultipleIds"`

Expected: **PASS** with elapsed < 800 ms.

- [ ] **Step 4: Format**

Run: `dotnet format backend/Anela.Heblo.sln --include backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/ClassifyInvoices/ClassifyInvoicesHandler.cs`

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/ClassifyInvoices/ClassifyInvoicesHandler.cs
git commit -m "perf: parallelize per-id invoice fetch in ClassifyInvoicesHandler

Replaces sequential foreach over GetInvoiceByIdAsync with Task.WhenAll under
a SemaphoreSlim(8) throttle. Fetch errors are now reported per-id instead of
aborting the whole request. Classification loop and batch-mode path unchanged."
```

---

## Task 3: Add the bounded-concurrency test

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/ClassifyInvoicesHandlerTests.cs`

Verifies FR-2: at most `MaxFetchConcurrency` (= 8) calls are in flight at any moment. Uses an `Interlocked` counter inside a hand-rolled fake (Moq's setup callbacks fire pre-await, which would race the assertion, so a real method body is clearer).

- [ ] **Step 1: Add a tracking fake client + the test**

Inside the existing `ClassifyInvoicesHandlerTests` class (above the closing brace, after the existing `Handle_FetchesInParallel_WhenMultipleIds` test), append the following nested helper class and test:

```csharp
    private sealed class TrackingFakeClient : IReceivedInvoicesClient
    {
        private int _inFlight;
        public int MaxObservedInFlight;
        private readonly TimeSpan _delay;

        public TrackingFakeClient(TimeSpan delay)
        {
            _delay = delay;
        }

        public Task<List<ReceivedInvoiceDto>> GetUnclassifiedInvoicesAsync() =>
            Task.FromResult(new List<ReceivedInvoiceDto>());

        public async Task<ReceivedInvoiceDto?> GetInvoiceByIdAsync(string invoiceId)
        {
            var current = Interlocked.Increment(ref _inFlight);
            // Update peak using atomic CAS loop.
            int observed;
            do
            {
                observed = MaxObservedInFlight;
                if (current <= observed) break;
            }
            while (Interlocked.CompareExchange(ref MaxObservedInFlight, current, observed) != observed);

            try
            {
                await Task.Delay(_delay);
                return new ReceivedInvoiceDto
                {
                    InvoiceNumber = invoiceId,
                    CompanyName = $"Company-{invoiceId}",
                    TotalAmount = 100m,
                    Description = "test"
                };
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }
    }

    [Fact]
    public async Task Handle_RespectsConcurrencyLimit()
    {
        // Arrange: 30 ids, 100 ms per fake fetch — guarantees the throttle saturates.
        const int MaxFetchConcurrency = 8; // mirrors the handler's private constant
        var ids = Enumerable.Range(1, 30).Select(i => $"INV-{i}").ToList();
        var fakeClient = new TrackingFakeClient(TimeSpan.FromMilliseconds(100));

        var handler = BuildHandler(fakeClient);
        var request = new ClassifyInvoicesRequest { InvoiceIds = ids };

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.TotalInvoicesProcessed.Should().Be(30);
        fakeClient.MaxObservedInFlight.Should().BeLessThanOrEqualTo(MaxFetchConcurrency,
            "the SemaphoreSlim throttle must cap concurrent Flexi calls");
    }
```

- [ ] **Step 2: Run the new test**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ClassifyInvoicesHandlerTests.Handle_RespectsConcurrencyLimit"`

Expected: **PASS** with `MaxObservedInFlight <= 8`.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/ClassifyInvoicesHandlerTests.cs
git commit -m "test: verify ClassifyInvoicesHandler caps fetch concurrency at 8"
```

---

## Task 4: Add the not-found preservation test

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/ClassifyInvoicesHandlerTests.cs`

Verifies FR-3: when an id returns `null` from `GetInvoiceByIdAsync`, the handler increments `Errors`, appends `"Invoice {id} not found"` to `ErrorMessages`, and the remaining ids still flow into classification.

- [ ] **Step 1: Append the test**

Append inside `ClassifyInvoicesHandlerTests`:

```csharp
    [Fact]
    public async Task Handle_AppendsNotFoundError_AndContinues()
    {
        // Arrange: B is missing; A and C succeed.
        var clientMock = new Mock<IReceivedInvoicesClient>();
        clientMock.Setup(c => c.GetInvoiceByIdAsync("A")).ReturnsAsync(Invoice("A"));
        clientMock.Setup(c => c.GetInvoiceByIdAsync("B")).ReturnsAsync((ReceivedInvoiceDto?)null);
        clientMock.Setup(c => c.GetInvoiceByIdAsync("C")).ReturnsAsync(Invoice("C"));

        var classifiedIds = new ConcurrentBag<string>();
        var classificationMock = new Mock<IInvoiceClassificationService>();
        classificationMock
            .Setup(s => s.ClassifyInvoiceAsync(It.IsAny<ReceivedInvoiceDto>()))
            .Returns<ReceivedInvoiceDto>(inv =>
            {
                classifiedIds.Add(inv.InvoiceNumber);
                return Task.FromResult(SuccessResult());
            });

        var handler = BuildHandler(clientMock.Object, classificationMock.Object);
        var request = new ClassifyInvoicesRequest { InvoiceIds = new List<string> { "A", "B", "C" } };

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.Errors.Should().Be(1);
        response.ErrorMessages.Should().ContainSingle(m => m == "Invoice B not found");
        response.SuccessfulClassifications.Should().Be(2);
        classifiedIds.Should().BeEquivalentTo(new[] { "A", "C" });
    }
```

- [ ] **Step 2: Run the new test**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ClassifyInvoicesHandlerTests.Handle_AppendsNotFoundError_AndContinues"`

Expected: **PASS**.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/ClassifyInvoicesHandlerTests.cs
git commit -m "test: cover not-found error path in ClassifyInvoicesHandler"
```

---

## Task 5: Add the per-id fetch-exception isolation test

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/ClassifyInvoicesHandlerTests.cs`

Verifies FR-4: if `GetInvoiceByIdAsync` throws for a single id, that id is reported as a fetch error and the remaining ids still classify. This is the **behavior change** vs. the pre-change handler (which would have aborted the whole request), so the assertion shape is intentional.

- [ ] **Step 1: Append the test**

Append inside `ClassifyInvoicesHandlerTests`:

```csharp
    [Fact]
    public async Task Handle_FetchExceptionIsolatedToOneId()
    {
        // Arrange: B throws; A and C succeed.
        var clientMock = new Mock<IReceivedInvoicesClient>();
        clientMock.Setup(c => c.GetInvoiceByIdAsync("A")).ReturnsAsync(Invoice("A"));
        clientMock.Setup(c => c.GetInvoiceByIdAsync("B")).ThrowsAsync(new InvalidOperationException("flexi-network-down"));
        clientMock.Setup(c => c.GetInvoiceByIdAsync("C")).ReturnsAsync(Invoice("C"));

        var classifiedIds = new ConcurrentBag<string>();
        var classificationMock = new Mock<IInvoiceClassificationService>();
        classificationMock
            .Setup(s => s.ClassifyInvoiceAsync(It.IsAny<ReceivedInvoiceDto>()))
            .Returns<ReceivedInvoiceDto>(inv =>
            {
                classifiedIds.Add(inv.InvoiceNumber);
                return Task.FromResult(SuccessResult());
            });

        var handler = BuildHandler(clientMock.Object, classificationMock.Object);
        var request = new ClassifyInvoicesRequest { InvoiceIds = new List<string> { "A", "B", "C" } };

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.Errors.Should().Be(1);
        response.ErrorMessages.Should().ContainSingle()
            .Which.Should().Be("Invoice B: fetch failed: flexi-network-down");
        response.SuccessfulClassifications.Should().Be(2);
        classifiedIds.Should().BeEquivalentTo(new[] { "A", "C" });
    }
```

- [ ] **Step 2: Run the new test**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ClassifyInvoicesHandlerTests.Handle_FetchExceptionIsolatedToOneId"`

Expected: **PASS**.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/ClassifyInvoicesHandlerTests.cs
git commit -m "test: isolate per-id Flexi fetch exceptions in ClassifyInvoicesHandler"
```

---

## Task 6: Add the input-order error test

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/ClassifyInvoicesHandlerTests.cs`

Verifies FR-5: `ErrorMessages` entries appear in the same order as `request.InvoiceIds`, even when fetches complete out of order due to per-call delays. Uses staggered delays so the natural completion order would be the *reverse* of input order, which would fail the test if the handler did not reassemble in input order.

- [ ] **Step 1: Append the test**

Append inside `ClassifyInvoicesHandlerTests`:

```csharp
    [Fact]
    public async Task Handle_PreservesInputOrderOfErrors()
    {
        // Arrange: input order [A, B, C, D, E].
        // A: throws (slowest)
        // B: not found
        // C: throws
        // D: not found
        // E: succeeds (fastest)
        // If the handler appended errors in completion order, E would not appear in errors and the rest
        // would be reversed; we assert input-order to lock in FR-5.
        var clientMock = new Mock<IReceivedInvoicesClient>();

        clientMock.Setup(c => c.GetInvoiceByIdAsync("A"))
            .Returns<string>(async _ =>
            {
                await Task.Delay(250);
                throw new InvalidOperationException("err-A");
            });
        clientMock.Setup(c => c.GetInvoiceByIdAsync("B"))
            .Returns<string>(async _ =>
            {
                await Task.Delay(200);
                return null;
            });
        clientMock.Setup(c => c.GetInvoiceByIdAsync("C"))
            .Returns<string>(async _ =>
            {
                await Task.Delay(150);
                throw new InvalidOperationException("err-C");
            });
        clientMock.Setup(c => c.GetInvoiceByIdAsync("D"))
            .Returns<string>(async _ =>
            {
                await Task.Delay(100);
                return null;
            });
        clientMock.Setup(c => c.GetInvoiceByIdAsync("E"))
            .Returns<string>(async _ =>
            {
                await Task.Delay(50);
                return Invoice("E");
            });

        var handler = BuildHandler(clientMock.Object);
        var request = new ClassifyInvoicesRequest { InvoiceIds = new List<string> { "A", "B", "C", "D", "E" } };

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert: errors ordered A, B, C, D — strict input order.
        response.Errors.Should().Be(4);
        response.ErrorMessages.Should().Equal(
            "Invoice A: fetch failed: err-A",
            "Invoice B not found",
            "Invoice C: fetch failed: err-C",
            "Invoice D not found");
        response.SuccessfulClassifications.Should().Be(1);
    }
```

- [ ] **Step 2: Run the new test**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ClassifyInvoicesHandlerTests.Handle_PreservesInputOrderOfErrors"`

Expected: **PASS**.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/ClassifyInvoicesHandlerTests.cs
git commit -m "test: assert ClassifyInvoicesHandler reports fetch errors in input order"
```

---

## Task 7: Add the batch-mode-unchanged test

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/ClassifyInvoicesHandlerTests.cs`

Verifies FR-6: when `InvoiceIds` is null or empty, the handler calls `GetUnclassifiedInvoicesAsync` exactly once and never calls `GetInvoiceByIdAsync`.

- [ ] **Step 1: Append the test**

Append inside `ClassifyInvoicesHandlerTests`:

```csharp
    [Fact]
    public async Task Handle_BatchModeUnchanged_NullInvoiceIds()
    {
        // Arrange: null InvoiceIds means batch mode.
        var clientMock = new Mock<IReceivedInvoicesClient>();
        clientMock.Setup(c => c.GetUnclassifiedInvoicesAsync())
            .ReturnsAsync(new List<ReceivedInvoiceDto> { Invoice("X"), Invoice("Y") });

        var handler = BuildHandler(clientMock.Object);
        var request = new ClassifyInvoicesRequest { InvoiceIds = null };

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.TotalInvoicesProcessed.Should().Be(2);
        clientMock.Verify(c => c.GetUnclassifiedInvoicesAsync(), Times.Once);
        clientMock.Verify(c => c.GetInvoiceByIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Handle_BatchModeUnchanged_EmptyInvoiceIds()
    {
        // Arrange: empty list also means batch mode.
        var clientMock = new Mock<IReceivedInvoicesClient>();
        clientMock.Setup(c => c.GetUnclassifiedInvoicesAsync())
            .ReturnsAsync(new List<ReceivedInvoiceDto>());

        var handler = BuildHandler(clientMock.Object);
        var request = new ClassifyInvoicesRequest { InvoiceIds = new List<string>() };

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.TotalInvoicesProcessed.Should().Be(0);
        clientMock.Verify(c => c.GetUnclassifiedInvoicesAsync(), Times.Once);
        clientMock.Verify(c => c.GetInvoiceByIdAsync(It.IsAny<string>()), Times.Never);
    }
```

- [ ] **Step 2: Run the new tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ClassifyInvoicesHandlerTests.Handle_BatchModeUnchanged"`

Expected: **PASS** (2 tests).

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/ClassifyInvoicesHandlerTests.cs
git commit -m "test: confirm batch-mode path of ClassifyInvoicesHandler is unchanged"
```

---

## Task 8: Full validation pass

**Files:** none modified — verification only.

This is the pre-merge gate from `CLAUDE.md` ("Validation before completion"). All seven tests must pass alongside the rest of the test suite, the solution must build clean, and `dotnet format` must report no diffs.

- [ ] **Step 1: Build the entire backend**

Run: `dotnet build backend/Anela.Heblo.sln`

Expected: **Build succeeded. 0 Warning(s). 0 Error(s).**

- [ ] **Step 2: Run the full test project**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`

Expected: **Passed!** with the new 7 `ClassifyInvoicesHandlerTests` tests included and no pre-existing test regressions.

- [ ] **Step 3: Verify formatting is clean**

Run: `dotnet format backend/Anela.Heblo.sln --verify-no-changes`

Expected: exit code 0, no formatting diffs reported.

If exit code is non-zero, run `dotnet format backend/Anela.Heblo.sln` and commit the formatting fix:

```bash
git add -u
git commit -m "chore: dotnet format"
```

- [ ] **Step 4: Sanity-check the diff against the handler**

Run: `git diff main -- backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/ClassifyInvoices/ClassifyInvoicesHandler.cs`

Expected diff invariants:
- Lines 56–61 (batch mode block) are present and visually identical to the pre-change version.
- The classification `foreach` loop body is unchanged.
- The new `using System.Diagnostics;` directive is the only added top-level using.
- The new private `MaxFetchConcurrency` constant, `FetchOneAsync` method, and `FetchOutcome` record struct are present.

If any of these invariants is violated, return to Task 2 and trim the change to the surgical minimum.

---

## Spec Coverage Map

| Spec Requirement | Task(s) |
|------------------|---------|
| FR-1 Parallelize specific-invoice fetch | 1 (test), 2 (impl) |
| FR-2 Bounded concurrency (cap = 8) | 2 (impl), 3 (test) |
| FR-3 Preserve per-invoice error reporting | 2 (impl), 4 (test) |
| FR-4 Surface fetch-phase exceptions per invoice | 2 (impl), 5 (test) |
| FR-5 Deterministic error ordering (input order) | 2 (impl, comment + sequential aggregation), 6 (test) |
| FR-6 Batch mode unchanged | 2 (impl: lines preserved), 7 (test) |
| NFR-1 Performance target (P50 ≤ 3 s @ 20 ids) | 1 (timing test ≤ 800 ms @ 10 ids/200 ms) |
| NFR-2 Reliability + cancellation at throttle | 2 (`throttle.WaitAsync(cancellationToken)`) |
| NFR-3 `private const int MaxFetchConcurrency = 8` | 2 |
| NFR-4 Observability — preserve existing logs + add debug | 2 (`LogDebug` after `Task.WhenAll`) |
| NFR-5 No security surface change | inherent — no new inputs |
| NFR-6 Backward compatibility (no DTO/interface change) | 2 (`IReceivedInvoicesClient` untouched) |

## Status: COMPLETE

Plan complete and saved to `docs/superpowers/plans/2026-05-06-parallelize-invoice-fetch.md`.
