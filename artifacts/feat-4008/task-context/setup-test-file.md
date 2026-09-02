### task: setup-test-file

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceSyncStatsHandlerTests.cs`

- [ ] **Step 1: Create the test file skeleton**

Create the file with the class scaffold, matching the constructor shape confirmed in the architecture review (2 dependencies only — `IIssuedInvoiceRepository` and `ILogger<GetIssuedInvoiceSyncStatsHandler>`):

```csharp
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Invoices.UseCases.GetIssuedInvoiceSyncStats;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Invoices;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Invoices;

public class GetIssuedInvoiceSyncStatsHandlerTests
{
    private readonly Mock<IIssuedInvoiceRepository> _repositoryMock;
    private readonly GetIssuedInvoiceSyncStatsHandler _handler;

    public GetIssuedInvoiceSyncStatsHandlerTests()
    {
        _repositoryMock = new Mock<IIssuedInvoiceRepository>();

        _handler = new GetIssuedInvoiceSyncStatsHandler(
            _repositoryMock.Object,
            Mock.Of<ILogger<GetIssuedInvoiceSyncStatsHandler>>());
    }
}
```

- [ ] **Step 2: Build to confirm the skeleton compiles**

Run: `cd backend && dotnet build test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: `Build succeeded.` (0 errors) — an empty test class with no `[Fact]` methods is valid.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceSyncStatsHandlerTests.cs
git commit -m "test(invoices): scaffold GetIssuedInvoiceSyncStatsHandlerTests"
```

---
