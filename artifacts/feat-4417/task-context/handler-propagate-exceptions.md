### task: handler-propagate-exceptions

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/GetInvoiceDetails/GetInvoiceDetailsHandler.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/GetInvoiceDetailsHandlerTests.cs`

- [ ] **Step 1: Write the failing tests** — add to `GetInvoiceDetailsHandlerTests` (existing usings already suffice):

```csharp
    [Fact]
    public async Task Handle_WhenClientThrows_PropagatesException()
    {
        // Arrange
        var expected = new HttpRequestException("Flexi unreachable");
        var clientMock = new Mock<IReceivedInvoicesClient>();
        clientMock.Setup(c => c.GetInvoiceByIdAsync("FV-001"))
                  .ThrowsAsync(expected);

        var handler = new GetInvoiceDetailsHandler(
            clientMock.Object,
            NullLogger<GetInvoiceDetailsHandler>.Instance,
            BuildMapper());

        // Act
        var act = () => handler.Handle(
            new GetInvoiceDetailsRequest { InvoiceId = "FV-001" },
            CancellationToken.None);

        // Assert
        var thrown = await act.Should().ThrowAsync<HttpRequestException>();
        thrown.Which.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task Handle_WhenMapperThrows_PropagatesException()
    {
        // Arrange
        var clientMock = new Mock<IReceivedInvoicesClient>();
        clientMock.Setup(c => c.GetInvoiceByIdAsync("FV-001"))
                  .ReturnsAsync(new ReceivedInvoice { InvoiceNumber = "FV-001", Labels = Array.Empty<string>() });
        var mapperMock = new Mock<IMapper>();
        mapperMock.Setup(m => m.Map<ApplicationContracts.ReceivedInvoiceDto>(It.IsAny<object>()))
                  .Throws(new InvalidOperationException("mapping failed"));

        var handler = new GetInvoiceDetailsHandler(
            clientMock.Object,
            NullLogger<GetInvoiceDetailsHandler>.Instance,
            mapperMock.Object);

        // Act
        var act = () => handler.Handle(
            new GetInvoiceDetailsRequest { InvoiceId = "FV-001" },
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("mapping failed");
    }
```

- [ ] **Step 2: Run tests, verify the two new ones fail**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~GetInvoiceDetailsHandlerTests"`
Expected: `Handle_WhenClientThrows_PropagatesException` and `Handle_WhenMapperThrows_PropagatesException` FAIL (no exception thrown); the other two PASS.

- [ ] **Step 3: Implement** — replace the body of `Handle` in `GetInvoiceDetailsHandler.cs` with:

```csharp
    public async Task<GetInvoiceDetailsResponse> Handle(GetInvoiceDetailsRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting details for invoice {InvoiceId}", request.InvoiceId);

        // GetInvoiceByIdAsync returns null when the invoice does not exist; any other failure
        // propagates to the global exception handler (HTTP 500) rather than masquerading as 404.
        var invoice = await _invoicesClient.GetInvoiceByIdAsync(request.InvoiceId);

        // Explicit null check: AutoMapper would otherwise allocate an empty destination,
        // breaking the API contract that returns `Invoice = null` when not found.
        var mapped = invoice is null ? null : _mapper.Map<Contracts.ReceivedInvoiceDto>(invoice);

        return new GetInvoiceDetailsResponse
        {
            Invoice = mapped,
            Found = invoice != null
        };
    }
```

- [ ] **Step 4: Run tests, verify all four pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~GetInvoiceDetailsHandlerTests"`
Expected: 4 passed.

- [ ] **Step 5: Format and build**

Run: `cd backend && dotnet format && dotnet build`
Expected: build succeeds, no warnings introduced.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/GetInvoiceDetails/GetInvoiceDetailsHandler.cs backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/GetInvoiceDetailsHandlerTests.cs
git commit -m "fix: let GetInvoiceDetailsHandler propagate infrastructure errors (#4417)"
```

