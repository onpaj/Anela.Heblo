# Custom Cooling Badge Text per Carrier Cooling Setting — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the user set an optional custom badge text per shipping-type cooling setting; that text replaces the hard-coded "CHLAZENÁ ZÁSILKA" on the expedition-list PDF when an order matches the cooling condition, falling back to the default when empty.

**Architecture:** Extend the existing `CarrierCoolingSetting` vertical slice (`Carrier + DeliveryHandling` keyed) with a nullable `CoolingText` column. The matrix GET/PUT endpoint carries the text alongside the cooling level; the expedition PDF source resolves the text per carrier+handling and the document renders it (or the default). The frontend matrix adds a textbox column per row that auto-saves on blur. Mirrors the existing `GiftSetting.Text` → `ExpeditionOrder.GiftBadgeText` → PDF gift-badge flow.

**Tech Stack:** .NET 8 (MediatR, EF Core/PostgreSQL, FluentValidation, QuestPDF), React + TypeScript (React Query), xUnit + FluentAssertions + Moq, Jest + @testing-library/react.

---

## Context

In **Nastavení expedice → Chlazení**, the user configures a cooling level (`Bez chlazení` / `L1` / `L2`) per shipping type — a row keyed by `(Carrier, DeliveryHandling)`. When an order matches the cooling condition at expedition time (`ExpeditionOrder.IsCooled`), the generated expedition-list PDF prints a hard-coded badge **"CHLAZENÁ ZÁSILKA"** (`ExpeditionProtocolDocument.cs:119`).

The user wants each setting row to carry an **optional custom badge text** via a new textbox column (after the `Bez chlazení / L1 / L2` radios). When set, that text shows on the PDF instead of the default; when empty, the default "CHLAZENÁ ZÁSILKA" is kept. The text is **per row** — rows use radios (single cooling level at a time), so one text per row is sufficient.

### Decisions / assumptions
- Custom text saves **on blur** (and Enter), reusing the existing single PUT endpoint — both `cooling` and `coolingText` are sent on every change so one upsert persists the row.
- Textbox is **always editable** (text on a `None` row simply never matches → harmless).
- Max length **50** chars (matches the gift-badge text convention; keeps the PDF badge tidy).
- Empty/whitespace text → null → PDF falls back to the default constant.
- The packing screen path (`ShoptetApiPackingOrderClient`) is **out of scope** — it does not generate the PDF; leave it untouched. Keep the existing `ResolveCarrierCooling` helper intact (it has callers + tests) and add a parallel text resolver.

---

## File Structure

**Backend — modify:**
- `backend/src/Anela.Heblo.Domain/Features/Logistics/CarrierCoolingSetting.cs` — add `CoolingText` property + ctor/update params.
- `backend/src/Anela.Heblo.Persistence/Logistics/CarrierCooling/CarrierCoolingSettingConfiguration.cs` — map `CoolingText`.
- `backend/src/Anela.Heblo.Persistence/Logistics/CarrierCooling/CarrierCoolingRepository.cs` — pass `CoolingText` through upsert.
- `backend/src/Anela.Heblo.Application/Features/CarrierCooling/Contracts/CarrierCoolingRowDto.cs` — add `CoolingText`.
- `backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/GetCarrierCoolingMatrix/GetCarrierCoolingMatrixHandler.cs` — populate `CoolingText`.
- `backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/SetCarrierCooling/SetCarrierCoolingRequest.cs` — add `CoolingText`.
- `backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/SetCarrierCooling/SetCarrierCoolingHandler.cs` — pass `CoolingText`.
- `backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/SetCarrierCooling/SetCarrierCoolingValidator.cs` — max-length rule.
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionProtocolData.cs` — add `CoolingText` to `ExpeditionOrder`.
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs` — resolve + assign text.
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionProtocolDocument.cs` — default constant + render text.

**Backend — create:**
- `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_AddCoolingTextToCarrierCoolingSettings.cs` — generated.
- `backend/test/Anela.Heblo.Tests/Domain/Logistics/CarrierCoolingSettingTests.cs` — entity test.

**Backend — modify tests:**
- `backend/test/Anela.Heblo.Tests/Application/CarrierCooling/GetCarrierCoolingMatrixHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Application/CarrierCooling/SetCarrierCoolingHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Application/CarrierCooling/SetCarrierCoolingValidatorTests.cs`
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition/ShoptetApiExpeditionListSource_CoolingMarkerTests.cs`
- `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ExpeditionProtocolDocumentTests.cs`

**Frontend — modify:**
- `frontend/src/api/hooks/useCarrierCooling.ts` — add `coolingText` to DTO + request + optimistic update.
- `frontend/src/components/customer/cooling/CarrierCoolingMatrix.tsx` — add textbox column via extracted row component.

**Frontend — create:**
- `frontend/src/components/customer/cooling/__tests__/CarrierCoolingMatrix.test.tsx`

---

## Task 1: Domain — add `CoolingText` to `CarrierCoolingSetting`

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Logistics/CarrierCoolingSetting.cs`
- Test: `backend/test/Anela.Heblo.Tests/Domain/Logistics/CarrierCoolingSettingTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Domain/Logistics/CarrierCoolingSettingTests.cs`:

```csharp
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Domain.Shared;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Domain.Logistics;

public class CarrierCoolingSettingTests
{
    [Fact]
    public void Constructor_StoresCoolingText_WhenProvided()
    {
        var setting = new CarrierCoolingSetting(
            Carriers.PPL, DeliveryHandling.Box, Cooling.L1, "user1", "MRAZ");

        setting.CoolingText.Should().Be("MRAZ");
    }

    [Fact]
    public void Constructor_DefaultsCoolingTextToNull_WhenOmitted()
    {
        var setting = new CarrierCoolingSetting(
            Carriers.PPL, DeliveryHandling.Box, Cooling.L1, "user1");

        setting.CoolingText.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CarrierCoolingSettingTests"`
Expected: compile error — `CarrierCoolingSetting` has no `CoolingText` and no 5-arg constructor.

- [ ] **Step 3: Write minimal implementation**

Replace the body of `backend/src/Anela.Heblo.Domain/Features/Logistics/CarrierCoolingSetting.cs`:

```csharp
using Anela.Heblo.Domain.Shared;

namespace Anela.Heblo.Domain.Features.Logistics;

public class CarrierCoolingSetting
{
    public Carriers Carrier { get; private set; }
    public DeliveryHandling DeliveryHandling { get; private set; }
    public Cooling Cooling { get; private set; }
    public string? CoolingText { get; private set; }
    public DateTime ModifiedAt { get; private set; }
    public string ModifiedBy { get; private set; } = null!;

    private CarrierCoolingSetting() { }

    public CarrierCoolingSetting(Carriers carrier, DeliveryHandling deliveryHandling, Cooling cooling, string modifiedBy, string? coolingText = null)
    {
        Carrier = carrier;
        DeliveryHandling = deliveryHandling;
        Cooling = cooling;
        CoolingText = coolingText;
        ModifiedBy = modifiedBy;
        ModifiedAt = DateTime.UtcNow;
    }

    internal void UpdateCooling(Cooling cooling, string modifiedBy, string? coolingText = null)
    {
        Cooling = cooling;
        CoolingText = coolingText;
        ModifiedBy = modifiedBy;
        ModifiedAt = DateTime.UtcNow;
    }
}
```

Note: `coolingText` is the **last, optional** parameter so existing 4-arg call sites (`SetCarrierCoolingHandler` and the three test files) keep compiling unchanged.

- [ ] **Step 4: Run test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CarrierCoolingSettingTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Logistics/CarrierCoolingSetting.cs \
        backend/test/Anela.Heblo.Tests/Domain/Logistics/CarrierCoolingSettingTests.cs
git commit -m "feat: add CoolingText to CarrierCoolingSetting entity"
```

---

## Task 2: Persistence — map `CoolingText`, upsert it, add migration

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/Logistics/CarrierCooling/CarrierCoolingSettingConfiguration.cs:25-28`
- Modify: `backend/src/Anela.Heblo.Persistence/Logistics/CarrierCooling/CarrierCoolingRepository.cs:33`
- Create: `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_AddCoolingTextToCarrierCoolingSettings.cs` (generated)

- [ ] **Step 1: Map the column in EF configuration**

In `CarrierCoolingSettingConfiguration.cs`, add after the `Cooling` property block (after line 28):

```csharp
        builder.Property(e => e.CoolingText)
            .HasMaxLength(50);
```

(No `.IsRequired()` — the column is nullable.)

- [ ] **Step 2: Pass `CoolingText` through the upsert**

In `CarrierCoolingRepository.cs`, change line 33 from:

```csharp
            existing.UpdateCooling(setting.Cooling, setting.ModifiedBy);
```

to:

```csharp
            existing.UpdateCooling(setting.Cooling, setting.ModifiedBy, setting.CoolingText);
```

- [ ] **Step 3: Generate the migration**

Run:

```bash
dotnet ef migrations add AddCoolingTextToCarrierCoolingSettings \
  --project backend/src/Anela.Heblo.Persistence \
  --startup-project backend/src/Anela.Heblo.API
```

Expected generated `Up`:

```csharp
migrationBuilder.AddColumn<string>(
    name: "CoolingText",
    schema: "public",
    table: "CarrierCoolingSettings",
    type: "character varying(50)",
    maxLength: 50,
    nullable: true);
```

Expected `Down`:

```csharp
migrationBuilder.DropColumn(
    name: "CoolingText",
    schema: "public",
    table: "CarrierCoolingSettings");
```

Verify the diff contains **only** the new column (plus the model-snapshot update) — no unintended changes. If anything else appears, abort with `dotnet ef migrations remove --project backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API` and investigate.

> **Do not run `dotnet ef database update`.** Per `CLAUDE.md`, migrations are applied manually by the user.

- [ ] **Step 4: Build to verify**

Run: `cd backend && dotnet build`
Expected: build succeeds.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Logistics/CarrierCooling/ \
        backend/src/Anela.Heblo.Persistence/Migrations/
git commit -m "feat: persist CoolingText for carrier cooling settings"
```

---

## Task 3: Application — carry `CoolingText` through GET and PUT

**Files:**
- Modify: `.../CarrierCooling/Contracts/CarrierCoolingRowDto.cs`
- Modify: `.../CarrierCooling/UseCases/GetCarrierCoolingMatrix/GetCarrierCoolingMatrixHandler.cs`
- Modify: `.../CarrierCooling/UseCases/SetCarrierCooling/SetCarrierCoolingRequest.cs`
- Modify: `.../CarrierCooling/UseCases/SetCarrierCooling/SetCarrierCoolingHandler.cs:38-42`
- Modify: `.../CarrierCooling/UseCases/SetCarrierCooling/SetCarrierCoolingValidator.cs`
- Test: `backend/test/Anela.Heblo.Tests/Application/CarrierCooling/{GetCarrierCoolingMatrixHandlerTests,SetCarrierCoolingHandlerTests,SetCarrierCoolingValidatorTests}.cs`

- [ ] **Step 1: Write the failing tests**

Add to `GetCarrierCoolingMatrixHandlerTests.cs`:

```csharp
    [Fact]
    public async Task Handle_ReturnsStoredCoolingText_WhenSettingExists()
    {
        _catalogMock.Setup(c => c.GetAvailableDeliveryOptions()).Returns(
            new List<(Carriers, DeliveryHandling)> { (Carriers.PPL, DeliveryHandling.Box) }.AsReadOnly());

        _repositoryMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CarrierCoolingSetting>
            {
                new(Carriers.PPL, DeliveryHandling.Box, Cooling.L1, "user1", "MRAZ"),
            });

        var result = await _sut.Handle(new GetCarrierCoolingMatrixRequest(), CancellationToken.None);

        result.Groups[0].Rows[0].CoolingText.Should().Be("MRAZ");
    }

    [Fact]
    public async Task Handle_ReturnsNullCoolingText_WhenNoStoredSetting()
    {
        _catalogMock.Setup(c => c.GetAvailableDeliveryOptions()).Returns(
            new List<(Carriers, DeliveryHandling)> { (Carriers.Zasilkovna, DeliveryHandling.NaRuky) }.AsReadOnly());
        _repositoryMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CarrierCoolingSetting>());

        var result = await _sut.Handle(new GetCarrierCoolingMatrixRequest(), CancellationToken.None);

        result.Groups[0].Rows[0].CoolingText.Should().BeNull();
    }
```

Add to `SetCarrierCoolingHandlerTests.cs`:

```csharp
    [Fact]
    public async Task Handle_PersistsCoolingText_WhenProvided()
    {
        SetupValidCombo(Carriers.PPL, DeliveryHandling.NaRuky);
        var request = new SetCarrierCoolingRequest
        {
            Carrier = Carriers.PPL,
            DeliveryHandling = DeliveryHandling.NaRuky,
            Cooling = Cooling.L1,
            CoolingText = "MRAZ",
            ModifiedBy = "user-123",
        };
        _repositoryMock
            .Setup(r => r.UpsertAsync(It.IsAny<CarrierCoolingSetting>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await CreateSut().Handle(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        _repositoryMock.Verify(
            r => r.UpsertAsync(
                It.Is<CarrierCoolingSetting>(s => s.CoolingText == "MRAZ"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
```

Add to `SetCarrierCoolingValidatorTests.cs`:

```csharp
    [Fact]
    public void Validator_FailsForCoolingTextExceedingMaxLength()
    {
        SetupCatalog((Carriers.PPL, DeliveryHandling.NaRuky));
        var validator = CreateValidator();
        var request = new SetCarrierCoolingRequest
        {
            Carrier = Carriers.PPL,
            DeliveryHandling = DeliveryHandling.NaRuky,
            Cooling = Cooling.L1,
            CoolingText = new string('x', 51),
            ModifiedBy = "user-123",
        };

        var result = validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.CoolingText);
    }

    [Fact]
    public void Validator_PassesForCoolingTextAtMaxLength()
    {
        SetupCatalog((Carriers.PPL, DeliveryHandling.NaRuky));
        var validator = CreateValidator();
        var request = new SetCarrierCoolingRequest
        {
            Carrier = Carriers.PPL,
            DeliveryHandling = DeliveryHandling.NaRuky,
            Cooling = Cooling.L1,
            CoolingText = new string('x', 50),
            ModifiedBy = "user-123",
        };

        var result = validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.CoolingText);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CarrierCooling"`
Expected: compile errors — `CarrierCoolingRowDto.CoolingText` and `SetCarrierCoolingRequest.CoolingText` do not exist.

- [ ] **Step 3: Implement the contract + request + handlers + validator**

`Contracts/CarrierCoolingRowDto.cs` — add the property:

```csharp
public class CarrierCoolingRowDto
{
    public DeliveryHandling DeliveryHandling { get; set; }
    public Cooling Cooling { get; set; }
    public string? CoolingText { get; set; }
}
```

`UseCases/SetCarrierCooling/SetCarrierCoolingRequest.cs` — add the property:

```csharp
public class SetCarrierCoolingRequest : IRequest<SetCarrierCoolingResponse>
{
    public Carriers Carrier { get; set; }
    public DeliveryHandling DeliveryHandling { get; set; }
    public Cooling Cooling { get; set; }
    public string? CoolingText { get; set; }
    public string ModifiedBy { get; set; } = string.Empty;
}
```

`GetCarrierCoolingMatrixHandler.cs` — replace the body of `Handle` (lines 23-40) with:

```csharp
        var available = _catalog.GetAvailableDeliveryOptions();
        var stored = await _repository.GetAllAsync(cancellationToken);
        var storedLookup = stored.ToDictionary(s => (s.Carrier, s.DeliveryHandling));

        var groups = available
            .GroupBy(x => x.Carrier)
            .Select(g => new CarrierGroupDto
            {
                Carrier = g.Key,
                Rows = g.Select(x =>
                {
                    storedLookup.TryGetValue((x.Carrier, x.Handling), out var setting);
                    return new CarrierCoolingRowDto
                    {
                        DeliveryHandling = x.Handling,
                        Cooling = setting?.Cooling ?? Cooling.None,
                        CoolingText = setting?.CoolingText,
                    };
                }).ToList(),
            })
            .ToList();

        return new GetCarrierCoolingMatrixResponse { Groups = groups };
```

`SetCarrierCoolingHandler.cs` — change the `new CarrierCoolingSetting(...)` (lines 38-42) to pass the text:

```csharp
        var setting = new CarrierCoolingSetting(
            request.Carrier,
            request.DeliveryHandling,
            request.Cooling,
            request.ModifiedBy,
            request.CoolingText);
```

`SetCarrierCoolingValidator.cs` — add a max-length rule after the existing `Cooling` rule:

```csharp
        RuleFor(x => x.CoolingText).MaximumLength(50);
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CarrierCooling"`
Expected: PASS (existing tests + the 5 new ones).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/CarrierCooling/ \
        backend/test/Anela.Heblo.Tests/Application/CarrierCooling/
git commit -m "feat: carry CoolingText through carrier cooling GET and PUT"
```

---

## Task 4: PDF source — resolve and assign `CoolingText` per order

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionProtocolData.cs:22`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs`
- Test: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition/ShoptetApiExpeditionListSource_CoolingMarkerTests.cs`

- [ ] **Step 1: Write the failing test**

In `ShoptetApiExpeditionListSource_CoolingMarkerTests.cs`, replace the `BuildSource` helper (lines 169-211) so it can inject a custom cooling text and capture the generated data:

```csharp
    private ShoptetApiExpeditionListSource BuildSource(
        Mock<HttpMessageHandler> handler,
        ILogger<ShoptetApiExpeditionListSource>? logger = null,
        string? coolingText = null,
        Action<ExpeditionProtocolData>? captureData = null)
    {
        var http = new HttpClient(handler.Object) { BaseAddress = new Uri("https://test.myshoptet.com") };
        var client = new ShoptetOrderClient(http);

        var catalog = new Mock<ICatalogRepository>();
        catalog.Setup(x => x.GetByIdAsync(CooledProductCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogAggregate
            {
                ProductCode = CooledProductCode,
                ProductName = "Cooled Product",
                Properties = new CatalogProperties { Cooling = Cooling.L1 },
            });
        catalog.Setup(x => x.GetByIdAsync(NormalProductCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogAggregate
            {
                ProductCode = NormalProductCode,
                ProductName = "Normal Product",
                Properties = new CatalogProperties { Cooling = Cooling.None },
            });

        var carrierCooling = new Mock<ICarrierCoolingRepository>();
        carrierCooling.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CarrierCoolingSetting>
            {
                new(Carriers.Zasilkovna, DeliveryHandling.NaRuky, Cooling.L1, "test", coolingText),
            });

        var giftSettings = new Mock<IGiftSettingRepository>();
        giftSettings.Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(GiftSetting.CreateDefault());

        return new ShoptetApiExpeditionListSource(
            client,
            TimeProvider.System,
            catalog.Object,
            carrierCooling.Object,
            giftSettings.Object,
            logger ?? Mock.Of<ILogger<ShoptetApiExpeditionListSource>>(),
            data =>
            {
                captureData?.Invoke(data);
                return new byte[] { 0x25, 0x50, 0x44, 0x46 };
            });
    }
```

Then add a new test:

```csharp
    [Fact]
    public async Task CreatePickingList_CooledOrder_UsesCustomCoolingTextFromSetting()
    {
        ExpeditionProtocolData? captured = null;
        var handler = BuildHandler();
        var source = BuildSource(handler, coolingText: "MRAZ", captureData: d => captured = d);

        await source.CreatePickingList(BuildRequest(), null, CancellationToken.None);

        captured.Should().NotBeNull();
        var cooledOrder = captured!.Orders.Single(o => o.Code == CooledOrderCode);
        cooledOrder.CoolingText.Should().Be("MRAZ");
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd backend && dotnet test test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj --filter "FullyQualifiedName~CoolingMarker"`
Expected: compile error — `ExpeditionOrder` has no `CoolingText`.

- [ ] **Step 3: Add `CoolingText` to the order model**

In `ExpeditionProtocolData.cs`, add to `ExpeditionOrder` after line 22 (`GiftBadgeText`):

```csharp
    public string? CoolingText { get; set; }
```

- [ ] **Step 4: Resolve and assign the text in the source**

In `ShoptetApiExpeditionListSource.cs`, after building `coolingMatrix` (line ~88-90), add a parallel text matrix:

```csharp
        var coolingTextMatrix = allSettings.ToDictionary(
            s => (s.Carrier, s.DeliveryHandling),
            s => s.CoolingText);
```

In the per-order loop, right after line 110 (`expeditionOrder.CarrierCooling = ResolveCarrierCooling(...)`), add:

```csharp
                expeditionOrder.CoolingText = ResolveCarrierCoolingText(shippingGuid, coolingTextMatrix);
```

Add a new static helper next to `ResolveCarrierCooling` (after line 310). Keep `ResolveCarrierCooling` untouched — it has other callers (`ShoptetApiPackingOrderClient`) and existing tests:

```csharp
    internal static string? ResolveCarrierCoolingText(
        string shippingGuid,
        IReadOnlyDictionary<(Carriers, DeliveryHandling), string?> matrix)
    {
        if (!ShippingMethodRegistry.ByGuid.TryGetValue(shippingGuid, out var method))
            return null;

        var handling = ShippingMethodRegistry.ResolveDeliveryHandling(method);
        if (!handling.HasValue)
            return null;

        return matrix.TryGetValue((method.Carrier, handling.Value), out var text)
            ? text
            : null;
    }
```

- [ ] **Step 5: Run test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj --filter "FullyQualifiedName~CoolingMarker"`
Expected: PASS (existing 3 marker tests + the new one).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionProtocolData.cs \
        backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs \
        backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition/ShoptetApiExpeditionListSource_CoolingMarkerTests.cs
git commit -m "feat: resolve custom cooling text per carrier on expedition orders"
```

---

## Task 5: PDF document — render custom text with default fallback

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionProtocolDocument.cs` (badge text at line 119; add constant near other frost constants ~line 31)
- Test: `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ExpeditionProtocolDocumentTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `ExpeditionProtocolDocumentTests.cs`:

```csharp
    [Fact]
    public void Generate_WithCustomCoolingText_DoesNotThrow()
    {
        // Cooled order carrying a custom badge text must render without exception.
        var data = new ExpeditionProtocolData
        {
            CarrierDisplayName = "Zásilkovna",
            Orders = new List<ExpeditionOrder>
            {
                new()
                {
                    Code = "COOL002",
                    CustomerName = "Test",
                    Address = "Praha",
                    Phone = "123",
                    CarrierCooling = Cooling.L1,
                    CoolingText = "DRŽTE V CHLADU",
                    Items = new List<ExpeditionOrderItem>
                    {
                        new()
                        {
                            ProductCode = "C1",
                            Name = "Chlazený",
                            Variant = string.Empty,
                            WarehousePosition = "C1",
                            Quantity = 1,
                            StockCount = 5,
                            StockDemand = 1,
                            UnitPrice = 100m,
                            Unit = "ks",
                            Cooling = Cooling.L1,
                        },
                    },
                },
            },
        };

        var act = () => ExpeditionProtocolDocument.Generate(data);

        act.Should().NotThrow();
    }
```

(The PDF is binary; the established pattern asserts `DoesNotThrow` rather than rendered text. Visual confirmation is covered in the manual verification section.)

- [ ] **Step 2: Run test to verify it fails**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ExpeditionProtocolDocumentTests.Generate_WithCustomCoolingText_DoesNotThrow"`
Expected: compile error — `ExpeditionOrder.CoolingText` is already added in Task 4, so this compiles; the test will PASS even before the render change. That's acceptable — proceed to Step 3 to make the badge actually use the text. (If Task 4 is not yet merged, the compile error is the failing signal.)

- [ ] **Step 3: Render the custom text with fallback**

In `ExpeditionProtocolDocument.cs`, add the default constant near the frost-badge constants (after line 30 `FrostBadgeBorderThickness`):

```csharp
    private const string DefaultCoolingText = "CHLAZENÁ ZÁSILKA";
```

Change the badge text line (line 119) from:

```csharp
                                .Text("CHLAZENÁ ZÁSILKA")
```

to:

```csharp
                                .Text(string.IsNullOrWhiteSpace(order.CoolingText) ? DefaultCoolingText : order.CoolingText)
```

- [ ] **Step 4: Run the document tests to verify they pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ExpeditionProtocolDocumentTests"`
Expected: PASS (all existing tests + the new one).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionProtocolDocument.cs \
        backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ExpeditionProtocolDocumentTests.cs
git commit -m "feat: render custom cooling badge text with default fallback in expedition PDF"
```

---

## Task 6: Frontend — extend the hook types and optimistic update

**Files:**
- Modify: `frontend/src/api/hooks/useCarrierCooling.ts`

- [ ] **Step 1: Add `coolingText` to the DTO and request types**

In `useCarrierCooling.ts`, update the two interfaces:

```typescript
export interface CarrierCoolingRowDto {
  deliveryHandling: DeliveryHandling;
  cooling: Cooling;
  coolingText?: string | null;
}
```

```typescript
export interface SetCarrierCoolingRequest {
  carrier: Carriers;
  deliveryHandling: DeliveryHandling;
  cooling: Cooling;
  coolingText?: string | null;
}
```

- [ ] **Step 2: Carry `coolingText` in the optimistic cache update**

In `useSetCarrierCooling`'s `onMutate`, change the row-mapping branch (lines 90-94) from:

```typescript
                rows: group.rows.map((row) =>
                  row.deliveryHandling !== request.deliveryHandling
                    ? row
                    : { ...row, cooling: request.cooling }
                ),
```

to:

```typescript
                rows: group.rows.map((row) =>
                  row.deliveryHandling !== request.deliveryHandling
                    ? row
                    : { ...row, cooling: request.cooling, coolingText: request.coolingText ?? null }
                ),
```

- [ ] **Step 3: Type-check**

Run: `cd frontend && npx tsc --noEmit`
Expected: no new type errors in `useCarrierCooling.ts`.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/api/hooks/useCarrierCooling.ts
git commit -m "feat: add coolingText to carrier cooling hook types and optimistic update"
```

---

## Task 7: Frontend — add the custom-text column to the matrix

**Files:**
- Modify: `frontend/src/components/customer/cooling/CarrierCoolingMatrix.tsx`
- Test: `frontend/src/components/customer/cooling/__tests__/CarrierCoolingMatrix.test.tsx` (create)

- [ ] **Step 1: Write the failing test**

Create `frontend/src/components/customer/cooling/__tests__/CarrierCoolingMatrix.test.tsx`:

```tsx
import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import CarrierCoolingMatrix from '../CarrierCoolingMatrix';
import { CarrierGroupDto } from '../../../../api/hooks/useCarrierCooling';

const baseGroups: CarrierGroupDto[] = [
  {
    carrier: 'Zasilkovna',
    rows: [{ deliveryHandling: 'Box', cooling: 'L1', coolingText: null }],
  },
];

describe('CarrierCoolingMatrix', () => {
  it('shows the default badge text as placeholder when no custom text is set', () => {
    render(
      <CarrierCoolingMatrix
        groups={baseGroups}
        onSetCooling={jest.fn()}
        isSaving={false}
        savingRow={null}
      />
    );

    expect(screen.getByPlaceholderText('CHLAZENÁ ZÁSILKA')).toBeInTheDocument();
  });

  it('fires onSetCooling with the custom text on blur', () => {
    const onSetCooling = jest.fn();
    render(
      <CarrierCoolingMatrix
        groups={baseGroups}
        onSetCooling={onSetCooling}
        isSaving={false}
        savingRow={null}
      />
    );

    const input = screen.getByPlaceholderText('CHLAZENÁ ZÁSILKA');
    fireEvent.change(input, { target: { value: 'MRAZ' } });
    fireEvent.blur(input);

    expect(onSetCooling).toHaveBeenCalledWith({
      carrier: 'Zasilkovna',
      deliveryHandling: 'Box',
      cooling: 'L1',
      coolingText: 'MRAZ',
    });
  });

  it('includes the current custom text when a cooling level radio changes', () => {
    const onSetCooling = jest.fn();
    const groupsWithText: CarrierGroupDto[] = [
      { carrier: 'Zasilkovna', rows: [{ deliveryHandling: 'Box', cooling: 'L1', coolingText: 'MRAZ' }] },
    ];
    render(
      <CarrierCoolingMatrix
        groups={groupsWithText}
        onSetCooling={onSetCooling}
        isSaving={false}
        savingRow={null}
      />
    );

    fireEvent.click(screen.getByRole('radio', { name: /L2/ }));

    expect(onSetCooling).toHaveBeenCalledWith({
      carrier: 'Zasilkovna',
      deliveryHandling: 'Box',
      cooling: 'L2',
      coolingText: 'MRAZ',
    });
  });

  it('does not fire onSetCooling on blur when the text is unchanged', () => {
    const onSetCooling = jest.fn();
    render(
      <CarrierCoolingMatrix
        groups={baseGroups}
        onSetCooling={onSetCooling}
        isSaving={false}
        savingRow={null}
      />
    );

    const input = screen.getByPlaceholderText('CHLAZENÁ ZÁSILKA');
    fireEvent.blur(input);

    expect(onSetCooling).not.toHaveBeenCalled();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd frontend && npx jest src/components/customer/cooling/__tests__/CarrierCoolingMatrix.test.tsx`
Expected: FAIL — no textbox / placeholder rendered yet.

- [ ] **Step 3: Implement the textbox column via an extracted row component**

Replace the full contents of `frontend/src/components/customer/cooling/CarrierCoolingMatrix.tsx`:

```tsx
import { useState } from 'react';
import {
  Carriers,
  CarrierCoolingRowDto,
  CarrierGroupDto,
  Cooling,
  DeliveryHandling,
  SetCarrierCoolingRequest,
} from '../../../api/hooks/useCarrierCooling';

interface CarrierCoolingMatrixProps {
  groups: CarrierGroupDto[];
  onSetCooling: (request: SetCarrierCoolingRequest) => void;
  isSaving: boolean;
  savingRow: { carrier: Carriers; deliveryHandling: DeliveryHandling } | null;
}

const CARRIER_LABELS: Record<Carriers, string> = {
  Zasilkovna: 'Zásilkovna',
  PPL: 'PPL',
  GLS: 'GLS',
  Osobak: 'Osobní odběr',
};

const HANDLING_LABELS: Record<DeliveryHandling, string> = {
  NaRuky: 'Do ruky',
  Box: 'Box',
};

const COOLING_OPTIONS: { value: Cooling; label: string }[] = [
  { value: 'None', label: 'Bez chlazení' },
  { value: 'L1', label: 'L1' },
  { value: 'L2', label: 'L2' },
];

const DEFAULT_COOLING_TEXT = 'CHLAZENÁ ZÁSILKA';
const COOLING_TEXT_MAX_LENGTH = 50;

interface CarrierCoolingRowProps {
  carrier: Carriers;
  row: CarrierCoolingRowDto;
  onSetCooling: (request: SetCarrierCoolingRequest) => void;
  isSaving: boolean;
  isThisRowSaving: boolean;
}

function CarrierCoolingRow({
  carrier,
  row,
  onSetCooling,
  isSaving,
  isThisRowSaving,
}: CarrierCoolingRowProps) {
  const [text, setText] = useState<string>(row.coolingText ?? '');
  const radioName = `${carrier}-${row.deliveryHandling}`;

  const commitText = () => {
    const normalized = text.trim();
    const current = row.coolingText ?? '';
    if (normalized === current) return;
    onSetCooling({
      carrier,
      deliveryHandling: row.deliveryHandling,
      cooling: row.cooling,
      coolingText: normalized === '' ? null : normalized,
    });
  };

  return (
    <div className="flex items-center px-3 py-2 gap-4">
      <span className="w-20 text-sm text-gray-700 flex-shrink-0">
        {HANDLING_LABELS[row.deliveryHandling] ?? String(row.deliveryHandling)}
      </span>
      <div className="flex gap-4">
        {COOLING_OPTIONS.map((option) => (
          <label key={option.value} className="flex items-center gap-2 cursor-pointer">
            <input
              type="radio"
              name={radioName}
              value={option.value}
              checked={row.cooling === option.value}
              onChange={() =>
                onSetCooling({
                  carrier,
                  deliveryHandling: row.deliveryHandling,
                  cooling: option.value,
                  coolingText: row.coolingText ?? null,
                })
              }
              disabled={isSaving}
              className="h-4 w-4 text-indigo-600 cursor-pointer"
            />
            <span className="text-sm text-gray-700">{option.label}</span>
          </label>
        ))}
      </div>
      <input
        type="text"
        value={text}
        maxLength={COOLING_TEXT_MAX_LENGTH}
        placeholder={DEFAULT_COOLING_TEXT}
        onChange={(e) => setText(e.target.value)}
        onBlur={commitText}
        onKeyDown={(e) => {
          if (e.key === 'Enter') {
            (e.target as HTMLInputElement).blur();
          }
        }}
        disabled={isSaving}
        className="flex-1 min-w-0 text-sm border border-gray-300 rounded px-2 py-1 focus:outline-none focus:ring-1 focus:ring-indigo-500"
      />
      {isThisRowSaving && (
        <span className="text-xs text-gray-400 ml-2 animate-pulse">Ukládám…</span>
      )}
    </div>
  );
}

function CarrierCoolingMatrix({ groups, onSetCooling, isSaving, savingRow }: CarrierCoolingMatrixProps) {
  return (
    <div className="space-y-3 p-4">
      {groups.map((group) => (
        <div
          key={group.carrier}
          className="bg-white border border-gray-200 rounded-lg shadow-sm overflow-hidden"
        >
          <div className="px-3 py-2 border-b border-gray-100 bg-gray-50">
            <h2 className="text-sm font-semibold text-gray-800">
              {CARRIER_LABELS[group.carrier] ?? `Dopravce ${group.carrier}`}
            </h2>
          </div>
          <div className="divide-y divide-gray-50">
            {group.rows.map((row) => {
              const isThisRowSaving =
                isSaving &&
                savingRow?.carrier === group.carrier &&
                savingRow?.deliveryHandling === row.deliveryHandling;

              return (
                <CarrierCoolingRow
                  key={row.deliveryHandling}
                  carrier={group.carrier}
                  row={row}
                  onSetCooling={onSetCooling}
                  isSaving={isSaving}
                  isThisRowSaving={isThisRowSaving}
                />
              );
            })}
          </div>
        </div>
      ))}
    </div>
  );
}

export default CarrierCoolingMatrix;
```

Note on local state: `text` is seeded from `row.coolingText` once. After a successful save the refetch returns the same normalized value, so the input stays consistent; a user's in-progress edit is not clobbered by an unrelated radio-triggered refetch. This is acceptable for this single-field row.

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd frontend && npx jest src/components/customer/cooling/__tests__/CarrierCoolingMatrix.test.tsx`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/customer/cooling/CarrierCoolingMatrix.tsx \
        frontend/src/components/customer/cooling/__tests__/CarrierCoolingMatrix.test.tsx
git commit -m "feat: add custom cooling text column to carrier cooling matrix"
```

---

## Task 8: Full validation

- [ ] **Step 1: Backend build + format + tests**

Run:
```bash
cd backend && dotnet build && dotnet format --verify-no-changes && dotnet test
```
Expected: build succeeds, no format changes, all tests pass. (If `dotnet format --verify-no-changes` reports changes, run `dotnet format` and amend.)

- [ ] **Step 2: Frontend build + lint + tests**

Run:
```bash
cd frontend && npm run build && npm run lint && npx jest src/components/customer/cooling src/api/hooks
```
Expected: build succeeds, lint clean, tests pass.

- [ ] **Step 3: Commit any format/lint fixups**

```bash
git add -A
git commit -m "chore: apply format and lint fixups for cooling custom text"
```

---

## Verification (end-to-end / manual)

1. **Apply the migration** to the target DB (manual — the user runs this):
   `dotnet ef database update --project backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API`
2. Start the app; open **Nastavení expedice → Chlazení**.
3. For a row (e.g. **Zásilkovna / Box**) set cooling to **L1** and type a custom text (e.g. `DRŽTE V CHLADU`). Click away (blur) — confirm "Ukládám…" then reload the page and confirm the text persisted.
4. Generate an expedition list for an order that matches the condition (L1 shipping + an L1 product). Confirm the PDF badge shows the **custom text** instead of "CHLAZENÁ ZÁSILKA".
5. Clear the custom text (empty) and blur; regenerate the list and confirm the badge falls back to **"CHLAZENÁ ZÁSILKA"**.
6. Optional visual check without a server: run `ExpeditionProtocolDocumentTests.Generate_CooledOrder_SavesToDiskForVisualInspection` (set `CoolingText` on the sample) and open the temp PDF (see `memory/patterns/pattern_pdf_visual_inspection.md`).

---

## Self-Review

- **Spec coverage:** custom text per setting (Tasks 1–3, 6–7); new textbox column after radios (Task 7); default "CHLAZENÁ ZÁSILKA" when unset (Task 5); custom text shown on PDF when condition matched (Tasks 4–5). All covered.
- **Type consistency:** `CoolingText` (C#) / `coolingText` (TS) used consistently; ctor signature `(carrier, deliveryHandling, cooling, modifiedBy, coolingText = null)` with the optional param last keeps all existing call sites compiling; `ResolveCarrierCoolingText` mirrors `ResolveCarrierCooling` and is additive (the original is preserved for `ShoptetApiPackingOrderClient` and its tests).
- **No placeholders:** every code step contains complete, runnable code and an exact command with expected output.
