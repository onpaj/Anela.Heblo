# ShippingMethodMapper Unit Tests Implementation Plan

**Goal:** Add a unit test suite for `ShippingMethodMapper.Map` covering all three GUID-resolution branches (no-GUID default, known-GUID lookup, unknown-GUID silent default + warning log) and both public constructors, closing the existing coverage gap with no production code changes.

**Architecture:** A single new xUnit test class, `ShippingMethodMapperTests`, is added to `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/`, alongside the existing sibling `BillingMethodMapperTests.cs` in the same directory/namespace. Each test constructs its own `IOptions<ShoptetApiSettings>` (via `Options.Create`) and `Mock<ILogger<ShippingMethodMapper>>` — no shared fixture, no `IClassFixture`. A private `CreateMapper` helper reduces setup duplication for the two-argument-constructor tests; two private verification helpers (`VerifyNoWarningLogged`, `VerifyWarningLoggedOnceContaining`) wrap the Moq `Mock<ILogger<T>>.Log(...)` verification idiom, copied verbatim from the working, already-compiling pattern in `InvoiceImportServiceTests.cs`.

**Tech Stack:** .NET 8, xUnit, Moq, FluentAssertions (all already referenced by the `Anela.Heblo.Tests` project — no new package references).

---

### task: add-shippingmethodmapper-tests

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShippingMethodMapperTests.cs`
- Test: same file (this is a test-only addition; there is no separate production file to modify)

Reference files read to produce this plan (do not modify):
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/IssuedInvoices/Mapping/ShippingMethodMapper.cs` — confirmed: namespace `Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Mapping`; constructors `ShippingMethodMapper(IOptions<ShoptetApiSettings>)` and `ShippingMethodMapper(IOptions<ShoptetApiSettings>, ILogger<ShippingMethodMapper>)`; `Map(ShoptetInvoiceShippingDto? shipping)` returns `ShippingMethod`; unknown-GUID branch calls `_logger.LogWarning("Unknown invoice shipping GUID '{Guid}' — defaulting to PickUp. Add to Shoptet:InvoiceShippingGuidMap config.", guid)` (single format arg, no exception passed — so the underlying `ILogger.Log` call carries `exception: null`).
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/IssuedInvoices/Model/ShoptetInvoiceShippingDto.cs` — confirmed: `public class ShoptetInvoiceShippingDto { string? Guid; string? Name; }` in namespace `Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Model`.
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiSettings.cs` — confirmed: `public class ShoptetApiSettings` in namespace `Anela.Heblo.Adapters.ShoptetApi.Orders`, with `public Dictionary<string, ShippingMethod> InvoiceShippingGuidMap { get; set; } = new();` (distinct from the unrelated `Dictionary<string, string> ShippingGuidMap`).
- `backend/src/Anela.Heblo.Domain/Features/Invoices/ShippingMethod.cs` — confirmed enum values in order: `PickUp, PPL, PPLParcelShop, ZasilkovnaDoRuky, Zasilkovna, GLS`, namespace `Anela.Heblo.Domain.Features.Invoices`. (Note: a second, unrelated `ShippingMethod` enum exists at `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShippingMethod.cs` — do not confuse the two; `ShippingMethodMapper.cs`'s `using Anela.Heblo.Domain.Features.Invoices;` confirms the Domain one is the correct type.)
- `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/BillingMethodMapperTests.cs` — style template: plain xUnit class in namespace `Anela.Heblo.Tests.Adapters.ShoptetApi`, `[Theory]`/`[InlineData]` for enumerated mappings, `[Fact]` for edge cases, `FluentAssertions`'s `result.Should().Be(...)`.
- `backend/test/Anela.Heblo.Tests/Features/Invoices/InvoiceImportServiceTests.cs` — confirmed this is where the working `Mock<ILogger<T>>` + `.Log(...)` verification pattern actually lives. Exact working shape (around line 203), used verbatim below:
  ```csharp
  _mockLogger.Verify(
      x => x.Log(
          LogLevel.Error,
          It.IsAny<EventId>(),
          It.Is<It.IsAnyType>((v, _) =>
              v.ToString()!.Contains("INV-FLEXI-400") &&
              v.ToString()!.Contains("Pole 'kod' je povinné.")),
          flexiError,
          It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
      Times.Once);
  ```
- `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` — confirmed `ImplicitUsings` and `Nullable` both `enable`, xUnit/Moq/FluentAssertions already referenced; no `TreatWarningsAsErrors`, so the `It.IsAnyType` matcher (already used successfully in `InvoiceImportServiceTests.cs`) compiles cleanly without extra suppressions.

---

- [ ] **Step 1: Write the failing test file with all test cases**

  Create `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShippingMethodMapperTests.cs` with the following exact content:

  ```csharp
  using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Mapping;
  using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Model;
  using Anela.Heblo.Adapters.ShoptetApi.Orders;
  using Anela.Heblo.Domain.Features.Invoices;
  using FluentAssertions;
  using Microsoft.Extensions.Logging;
  using Microsoft.Extensions.Options;
  using Moq;
  using Xunit;

  namespace Anela.Heblo.Tests.Adapters.ShoptetApi;

  public class ShippingMethodMapperTests
  {
      private const string KnownGuidPpl = "11111111-1111-1111-1111-111111111111";
      private const string KnownGuidZasilkovna = "22222222-2222-2222-2222-222222222222";
      private const string UnknownGuid = "99999999-9999-9999-9999-999999999999";

      private static ShippingMethodMapper CreateMapper(
          Dictionary<string, ShippingMethod>? guidMap,
          out Mock<ILogger<ShippingMethodMapper>> loggerMock)
      {
          loggerMock = new Mock<ILogger<ShippingMethodMapper>>();
          var settings = Options.Create(new ShoptetApiSettings
          {
              InvoiceShippingGuidMap = guidMap ?? new Dictionary<string, ShippingMethod>()
          });
          return new ShippingMethodMapper(settings, loggerMock.Object);
      }

      private static void VerifyNoWarningLogged(Mock<ILogger<ShippingMethodMapper>> loggerMock)
      {
          loggerMock.Verify(
              x => x.Log(
                  LogLevel.Warning,
                  It.IsAny<EventId>(),
                  It.IsAny<It.IsAnyType>(),
                  It.IsAny<Exception?>(),
                  It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
              Times.Never);
      }

      private static void VerifyWarningLoggedOnceContaining(Mock<ILogger<ShippingMethodMapper>> loggerMock, string expectedGuid)
      {
          loggerMock.Verify(
              x => x.Log(
                  LogLevel.Warning,
                  It.IsAny<EventId>(),
                  It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(expectedGuid)),
                  null,
                  It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
              Times.Once);
      }

      // FR-1: no shipping GUID -> PickUp, no warning logged

      [Fact]
      public void Map_ReturnsPickUp_WhenShippingIsNull()
      {
          // Arrange
          var mapper = CreateMapper(null, out var loggerMock);

          // Act
          var result = mapper.Map(null);

          // Assert
          result.Should().Be(ShippingMethod.PickUp);
          VerifyNoWarningLogged(loggerMock);
      }

      [Fact]
      public void Map_ReturnsPickUp_WhenGuidIsNull()
      {
          // Arrange
          var mapper = CreateMapper(null, out var loggerMock);

          // Act
          var result = mapper.Map(new ShoptetInvoiceShippingDto { Guid = null });

          // Assert
          result.Should().Be(ShippingMethod.PickUp);
          VerifyNoWarningLogged(loggerMock);
      }

      [Fact]
      public void Map_ReturnsPickUp_WhenGuidIsEmpty()
      {
          // Arrange
          var mapper = CreateMapper(null, out var loggerMock);

          // Act
          var result = mapper.Map(new ShoptetInvoiceShippingDto { Guid = "" });

          // Assert
          result.Should().Be(ShippingMethod.PickUp);
          VerifyNoWarningLogged(loggerMock);
      }

      // FR-2: known GUID -> configured method, no warning logged

      [Theory]
      [InlineData(KnownGuidPpl, ShippingMethod.PPL)]
      [InlineData(KnownGuidZasilkovna, ShippingMethod.Zasilkovna)]
      public void Map_ReturnsConfiguredMethod_WhenGuidIsKnown(string guid, ShippingMethod expected)
      {
          // Arrange
          var guidMap = new Dictionary<string, ShippingMethod>
          {
              [KnownGuidPpl] = ShippingMethod.PPL,
              [KnownGuidZasilkovna] = ShippingMethod.Zasilkovna
          };
          var mapper = CreateMapper(guidMap, out var loggerMock);

          // Act
          var result = mapper.Map(new ShoptetInvoiceShippingDto { Guid = guid });

          // Assert
          result.Should().Be(expected);
          VerifyNoWarningLogged(loggerMock);
      }

      // FR-3: unknown GUID -> PickUp + exactly one warning log containing the GUID

      [Fact]
      public void Map_ReturnsPickUpAndLogsWarning_WhenGuidIsUnknown_WithNonEmptyMap()
      {
          // Arrange
          var guidMap = new Dictionary<string, ShippingMethod>
          {
              [KnownGuidPpl] = ShippingMethod.PPL,
              [KnownGuidZasilkovna] = ShippingMethod.Zasilkovna
          };
          var mapper = CreateMapper(guidMap, out var loggerMock);

          // Act
          var result = mapper.Map(new ShoptetInvoiceShippingDto { Guid = UnknownGuid });

          // Assert
          result.Should().Be(ShippingMethod.PickUp);
          VerifyWarningLoggedOnceContaining(loggerMock, UnknownGuid);
      }

      [Fact]
      public void Map_ReturnsPickUpAndLogsWarning_WhenGuidIsUnknown_WithEmptyMap()
      {
          // Arrange
          var mapper = CreateMapper(new Dictionary<string, ShippingMethod>(), out var loggerMock);

          // Act
          var result = mapper.Map(new ShoptetInvoiceShippingDto { Guid = UnknownGuid });

          // Assert
          result.Should().Be(ShippingMethod.PickUp);
          VerifyWarningLoggedOnceContaining(loggerMock, UnknownGuid);
      }

      // FR-4: single-argument constructor works end-to-end (delegates to NullLogger)

      [Fact]
      public void Map_ReturnsPickUp_WhenConstructedWithSingleArgumentConstructor()
      {
          // Arrange
          var settings = Options.Create(new ShoptetApiSettings
          {
              InvoiceShippingGuidMap = new Dictionary<string, ShippingMethod>()
          });
          var mapper = new ShippingMethodMapper(settings);

          // Act
          var result = mapper.Map(new ShoptetInvoiceShippingDto { Guid = null });

          // Assert
          result.Should().Be(ShippingMethod.PickUp);
      }
  }
  ```

- [ ] **Step 2: Run the new tests to verify they pass**

  ```bash
  cd backend
  dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Anela.Heblo.Tests.Adapters.ShoptetApi.ShippingMethodMapperTests"
  ```

  Expected output: 8 tests discovered and passed (`Map_ReturnsPickUp_WhenShippingIsNull`, `Map_ReturnsPickUp_WhenGuidIsNull`, `Map_ReturnsPickUp_WhenGuidIsEmpty`, `Map_ReturnsConfiguredMethod_WhenGuidIsKnown` ×2 theory cases, `Map_ReturnsPickUpAndLogsWarning_WhenGuidIsUnknown_WithNonEmptyMap`, `Map_ReturnsPickUpAndLogsWarning_WhenGuidIsUnknown_WithEmptyMap`, `Map_ReturnsPickUp_WhenConstructedWithSingleArgumentConstructor`), 0 failed.

- [ ] **Step 3: Run the full sibling test folder to confirm no regressions**

  ```bash
  cd backend
  dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Anela.Heblo.Tests.Adapters.ShoptetApi"
  ```

  Expected output: all tests in `Adapters.ShoptetApi` pass, including the existing `BillingMethodMapperTests` plus the 8 new `ShippingMethodMapperTests`.

- [ ] **Step 4: Run `dotnet format` to confirm formatting compliance**

  ```bash
  cd backend
  dotnet format --verify-no-changes --include test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShippingMethodMapperTests.cs
  ```

  If this reports changes needed, run `dotnet format --include test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShippingMethodMapperTests.cs` (without `--verify-no-changes`) to apply them, then re-run Step 2 to confirm the tests still pass after formatting.

- [ ] **Step 5: Commit**

  ```bash
  git add backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShippingMethodMapperTests.cs
  git commit -m "test(shoptet-api): add ShippingMethodMapper unit test coverage"
  ```

  Verify: `git show --stat HEAD`

  Expected: a single file, `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShippingMethodMapperTests.cs`, listed as added.
