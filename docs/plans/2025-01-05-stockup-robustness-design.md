# Robustní StockUp Proces - Implementační plán

**Datum**: 2025-01-05
**Status**: Připraveno k implementaci

## Souhrn

Eliminace duplicitních naskladnění a zajištění spolehlivosti StockUpScenario operací přes Playwright pomocí state machine s 4 vrstvami ochrany.

## Problém

| Problém | Dopad |
|---------|-------|
| Žádné trackování provedených operací | Nelze zjistit co proběhlo |
| State change až PO stock-up | Crash = duplicita |
| Paralelní instance mohou zpracovat stejnou položku | Duplicita |
| Žádná verifikace úspěchu operace | Tichá selhání |
| BatchId je pouze timestamp | Nelze trasovat konkrétní položku |

## Řešení

### Architektura

```
Existující async trigger (Hangfire / UI)
    ↓
ProcessReceivedBoxesHandler / GiftPackageManufactureService
    ↓
StockUpOrchestrationService.ExecuteAsync(operation)
    ↓
┌─────────────────────────────────────────────────────────────┐
│ 1. Vytvoř StockUpOperation (Pending) + UNIQUE constraint    │
│ 2. Pre-check: existuje už v Shoptet historii?               │
│ 3. Playwright submit → stav Submitted                       │
│ 4. Playwright verify → stav Verified                        │
│ 5. Finalizace → stav Completed/Failed                       │
└─────────────────────────────────────────────────────────────┘
```

### 4 Vrstvy ochrany proti duplicitám

| Vrstva | Mechanismus | Kdy chrání |
|--------|-------------|------------|
| 1 | DB UNIQUE constraint na DocumentNumber | Paralelní instance |
| 2 | Optimistic concurrency na Box/Log entitě | Souběžné zpracování |
| 3 | Pre-submit check v Shoptet historii | Restart po crash |
| 4 | State machine pravidla | Opakované volání |

### Document Number formáty

| Kontext | Formát | Příklad |
|---------|--------|---------|
| Transport box | `BOX-{boxId:000000}-{productCode}` | `BOX-000123-DEO001030` |
| Gift package | `GPM-{logId:000000}-{productCode}` | `GPM-000456-GSET001` |

### Stavový diagram

```
┌─────────┐    ┌───────────┐    ┌──────────┐    ┌───────────┐
│ Pending │───▶│ Submitted │───▶│ Verified │───▶│ Completed │
└─────────┘    └───────────┘    └──────────┘    └───────────┘
     │              │                │
     ▼              ▼                ▼
┌──────────────────────────────────────────┐
│                 Failed                    │
│  (vyžaduje manuální review + retry)      │
└──────────────────────────────────────────┘
```

---

## Rozhodnutí

| Otázka | Rozhodnutí |
|--------|------------|
| Retry strategie | Žádné auto-retry. Při selhání Failed, manuální review |
| Cleanup | Nemazat nikdy - kompletní audit trail |
| Dashboard | Ano, základní seznam Failed operací s Retry tlačítkem |
| Architektura | Synchronní (handler už běží async) |

---

## Datový model

### StockUpOperation entita

**Soubor**: `Domain/Features/Catalog/Stock/StockUpOperation.cs`

```csharp
public class StockUpOperation : Entity<int>
{
    public string DocumentNumber { get; private set; }     // UNIQUE, např. "BOX-000123-DEO001030"
    public string ProductCode { get; private set; }        // Kód produktu
    public int Amount { get; private set; }                // Množství (kladné = příjem, záporné = výdej)
    public StockUpSourceType SourceType { get; private set; }  // Box / GiftPackage
    public int SourceId { get; private set; }              // ID boxu nebo GiftPackageManufactureLog
    public StockUpOperationState State { get; private set; }   // Pending → Submitted → Verified → Completed / Failed
    public DateTime CreatedAt { get; private set; }
    public DateTime? SubmittedAt { get; private set; }
    public DateTime? VerifiedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public string? ErrorMessage { get; private set; }

    // State transitions
    public void MarkAsSubmitted(DateTime timestamp);
    public void MarkAsVerified(DateTime timestamp);
    public void MarkAsCompleted(DateTime timestamp);
    public void MarkAsFailed(DateTime timestamp, string errorMessage);
}
```

### StockUpOperationState enum

**Soubor**: `Domain/Features/Catalog/Stock/StockUpOperationState.cs`

```csharp
public enum StockUpOperationState
{
    Pending = 0,      // Vytvořeno, čeká na odeslání
    Submitted = 1,    // Odesláno do Shoptet
    Verified = 2,     // Ověřeno v Shoptet historii
    Completed = 3,    // Úspěšně dokončeno
    Failed = 4        // Selhalo, vyžaduje manuální review
}
```

### StockUpSourceType enum

**Soubor**: `Domain/Features/Catalog/Stock/StockUpSourceType.cs`

```csharp
public enum StockUpSourceType
{
    TransportBox = 0,
    GiftPackageManufacture = 1
}
```

### IStockUpOperationRepository interface

**Soubor**: `Domain/Features/Catalog/Stock/IStockUpOperationRepository.cs`

```csharp
public interface IStockUpOperationRepository
{
    Task<StockUpOperation?> GetByDocumentNumberAsync(string documentNumber, CancellationToken ct = default);
    Task<StockUpOperation?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<List<StockUpOperation>> GetByStateAsync(StockUpOperationState state, CancellationToken ct = default);
    Task<List<StockUpOperation>> GetFailedOperationsAsync(CancellationToken ct = default);
    Task AddAsync(StockUpOperation operation, CancellationToken ct = default);
    Task UpdateAsync(StockUpOperation operation, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
```

---

## Implementační kroky

### Krok 1: Domain entities a interfaces

**Nové soubory:**

| Soubor | Popis |
|--------|-------|
| `backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/StockUpOperation.cs` | Entita |
| `backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/StockUpOperationState.cs` | Enum stavů |
| `backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/StockUpSourceType.cs` | Enum zdrojů |
| `backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/IStockUpOperationRepository.cs` | Repository interface |

### Krok 2: Persistence

**Nové soubory:**

| Soubor | Popis |
|--------|-------|
| `backend/src/Anela.Heblo.Persistence/Catalog/StockUpOperationConfiguration.cs` | EF Core config s UNIQUE constraint |
| `backend/src/Anela.Heblo.Persistence/Catalog/StockUpOperationRepository.cs` | Repository implementace |

**EF Configuration:**

```csharp
public class StockUpOperationConfiguration : IEntityTypeConfiguration<StockUpOperation>
{
    public void Configure(EntityTypeBuilder<StockUpOperation> builder)
    {
        builder.ToTable("StockUpOperations");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.DocumentNumber)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasIndex(x => x.DocumentNumber)
            .IsUnique();  // CRITICAL: Ochrana proti duplicitám

        builder.HasIndex(x => x.State);
        builder.HasIndex(x => new { x.SourceType, x.SourceId });
    }
}
```

**Migrace:**

```bash
dotnet ef migrations add AddStockUpOperations --project backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API
```

### Krok 3: VerifyStockUpScenario (Playwright)

**Nový soubor**: `backend/src/Adapters/Anela.Heblo.Adapters.Shoptet/Playwright/Scenarios/VerifyStockUpScenario.cs`

**Účel**: Přečíst historii skladu v Shoptet a ověřit, že záznam s daným číslem dokladu existuje.

**Flow:**
1. Navigovat na `/admin/sklad` → tab "Historie"
2. Vyplnit filtr "Číslo dokladu" s hledaným DocumentNumber
3. Kliknout "Filtrovat"
4. Zkontrolovat, zda tabulka obsahuje alespoň jeden řádek
5. Vrátit `bool` (existuje / neexistuje)

**Interface update** v `IEshopStockDomainService`:

```csharp
public interface IEshopStockDomainService
{
    Task StockUpAsync(StockUpRequest stockUpOrder);
    Task<bool> VerifyStockUpExistsAsync(string documentNumber);  // NOVÁ METODA
}
```

### Krok 4: StockUpOrchestrationService

**Nový soubor**: `backend/src/Anela.Heblo.Application/Features/Catalog/Services/StockUpOrchestrationService.cs`

**Účel**: Centralizovaná logika pro bezpečné stock-up operace se všemi 4 vrstvami ochrany.

```csharp
public class StockUpOrchestrationService : IStockUpOrchestrationService
{
    public async Task<StockUpOperationResult> ExecuteAsync(
        string documentNumber,
        string productCode,
        int amount,
        StockUpSourceType sourceType,
        int sourceId,
        CancellationToken ct = default)
    {
        // 1. Vytvoř StockUpOperation (UNIQUE constraint ochrana)
        var operation = new StockUpOperation(documentNumber, productCode, amount, sourceType, sourceId);

        try
        {
            await _repository.AddAsync(operation, ct);
            await _repository.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // Operace už existuje - načíst existující
            var existing = await _repository.GetByDocumentNumberAsync(documentNumber, ct);
            if (existing?.State == StockUpOperationState.Completed)
                return StockUpOperationResult.AlreadyCompleted(existing);
            if (existing?.State == StockUpOperationState.Failed)
                return StockUpOperationResult.PreviouslyFailed(existing);
            // V jiném stavu - skip
            return StockUpOperationResult.InProgress(existing);
        }

        // 2. Pre-check v Shoptet historii
        if (await _eshopService.VerifyStockUpExistsAsync(documentNumber))
        {
            operation.MarkAsCompleted(DateTime.UtcNow);
            await _repository.SaveChangesAsync(ct);
            return StockUpOperationResult.AlreadyInShoptet(operation);
        }

        // 3. Submit do Shoptet
        try
        {
            operation.MarkAsSubmitted(DateTime.UtcNow);
            await _repository.SaveChangesAsync(ct);

            var request = new StockUpRequest(productCode, amount, documentNumber);
            await _eshopService.StockUpAsync(request);
        }
        catch (Exception ex)
        {
            operation.MarkAsFailed(DateTime.UtcNow, $"Submit failed: {ex.Message}");
            await _repository.SaveChangesAsync(ct);
            return StockUpOperationResult.SubmitFailed(operation, ex);
        }

        // 4. Post-verify v Shoptet historii
        try
        {
            if (await _eshopService.VerifyStockUpExistsAsync(documentNumber))
            {
                operation.MarkAsVerified(DateTime.UtcNow);
                operation.MarkAsCompleted(DateTime.UtcNow);
                await _repository.SaveChangesAsync(ct);
                return StockUpOperationResult.Success(operation);
            }
            else
            {
                operation.MarkAsFailed(DateTime.UtcNow, "Verification failed: Record not found in Shoptet history");
                await _repository.SaveChangesAsync(ct);
                return StockUpOperationResult.VerificationFailed(operation);
            }
        }
        catch (Exception ex)
        {
            operation.MarkAsFailed(DateTime.UtcNow, $"Verification error: {ex.Message}");
            await _repository.SaveChangesAsync(ct);
            return StockUpOperationResult.VerificationError(operation, ex);
        }
    }
}
```

### Krok 5: Update ProcessReceivedBoxesHandler

**Soubor**: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ProcessReceivedBoxes/ProcessReceivedBoxesHandler.cs`

**Změny:**

```csharp
private async Task StockUpBoxItemsAsync(TransportBox box, CancellationToken cancellationToken)
{
    foreach (var item in box.Items)
    {
        // Nový formát DocumentNumber
        var documentNumber = $"BOX-{box.Id:000000}-{item.ProductCode}";

        _logger.LogDebug("Stocking up item: {DocumentNumber} - {ProductCode}, Amount: {Amount}",
            documentNumber, item.ProductCode, item.Amount);

        var result = await _stockUpOrchestrationService.ExecuteAsync(
            documentNumber,
            item.ProductCode,
            item.Amount,
            StockUpSourceType.TransportBox,
            box.Id,
            cancellationToken);

        if (!result.IsSuccess)
        {
            _logger.LogWarning("Stock up operation {DocumentNumber} result: {Status} - {Message}",
                documentNumber, result.Status, result.Message);

            // Pokud selhalo a není to "already completed", throw
            if (result.Status == StockUpResultStatus.Failed)
                throw new StockUpException(result.Message);
        }

        _logger.LogDebug("Successfully processed stock up: {DocumentNumber}, Status: {Status}",
            documentNumber, result.Status);
    }
}
```

### Krok 6: Update GiftPackageManufactureService

**Soubor**: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs`

**Změny:**

1. **Uložit GiftPackageManufactureLog PŘED stock-up operacemi** (aby bylo dostupné ID)
2. **Použít nový DocumentNumber formát pro každou položku**

```csharp
private async Task CreateManufactureAsync(...)
{
    // Vytvořit a ULOŽIT log PŘED stock-up
    var manufactureLog = new GiftPackageManufactureLog(...);
    await _manufactureLogRepository.AddAsync(manufactureLog);
    await _manufactureLogRepository.SaveChangesAsync();  // DŮLEŽITÉ: Získáme ID

    // Stock-up pro každou ingredient (záporné množství = spotřeba)
    foreach (var ingredient in ingredients)
    {
        var documentNumber = $"GPM-{manufactureLog.Id:000000}-{ingredient.ProductCode}";

        await _stockUpOrchestrationService.ExecuteAsync(
            documentNumber,
            ingredient.ProductCode,
            -ingredient.Amount,  // Záporné = spotřeba
            StockUpSourceType.GiftPackageManufacture,
            manufactureLog.Id,
            cancellationToken);
    }

    // Stock-up pro výsledný produkt (kladné množství = výroba)
    var outputDocNumber = $"GPM-{manufactureLog.Id:000000}-{giftPackageCode}";
    await _stockUpOrchestrationService.ExecuteAsync(
        outputDocNumber,
        giftPackageCode,
        quantity,  // Kladné = příjem
        StockUpSourceType.GiftPackageManufacture,
        manufactureLog.Id,
        cancellationToken);
}
```

### Krok 7: Dashboard pro Failed operace

#### Backend

**Nové soubory:**

| Soubor | Popis |
|--------|-------|
| `Application/Features/Catalog/UseCases/GetStockUpOperations/GetStockUpOperationsHandler.cs` | Seznam operací |
| `Application/Features/Catalog/UseCases/GetStockUpOperations/GetStockUpOperationsRequest.cs` | Request |
| `Application/Features/Catalog/UseCases/GetStockUpOperations/GetStockUpOperationsResponse.cs` | Response |
| `Application/Features/Catalog/UseCases/RetryStockUpOperation/RetryStockUpOperationHandler.cs` | Retry operace |
| `API/Controllers/StockUpOperationsController.cs` | API controller |

**Endpoints:**

```
GET  /api/stock-up-operations?state=Failed     → Seznam operací (filtrovatelný)
POST /api/stock-up-operations/{id}/retry       → Manuální retry
```

#### Frontend

**Nové soubory:**

| Soubor | Popis |
|--------|-------|
| `frontend/src/pages/StockOperationsPage.tsx` | Hlavní stránka |
| `frontend/src/api/hooks/useStockUpOperations.ts` | API hooks |

**UI komponenty:**
- Tabulka s operacemi: DocumentNumber, ProductCode, Amount, Source, State, ErrorMessage, CreatedAt
- Filtr podle stavu (All / Failed / Completed)
- Tlačítko "Retry" pro Failed operace
- Detail operace s historií stavů

### Krok 8: Testy

| Test | Soubor | Popis |
|------|--------|-------|
| Unit | `StockUpOrchestrationServiceTests.cs` | Testuje všechny scénáře orchestration service |
| Integration | `StockUpOperationRepositoryTests.cs` | Testuje UNIQUE constraint |
| E2E | `VerifyStockUpScenarioTests.cs` | Testuje Playwright scenario proti staging |

---

## Kritické soubory k vytvoření/úpravě

### Nové soubory

| Soubor | Layer |
|--------|-------|
| `Domain/Features/Catalog/Stock/StockUpOperation.cs` | Domain |
| `Domain/Features/Catalog/Stock/StockUpOperationState.cs` | Domain |
| `Domain/Features/Catalog/Stock/StockUpSourceType.cs` | Domain |
| `Domain/Features/Catalog/Stock/IStockUpOperationRepository.cs` | Domain |
| `Persistence/Catalog/StockUpOperationConfiguration.cs` | Persistence |
| `Persistence/Catalog/StockUpOperationRepository.cs` | Persistence |
| `Adapters/Shoptet/Playwright/Scenarios/VerifyStockUpScenario.cs` | Adapter |
| `Application/Features/Catalog/Services/StockUpOrchestrationService.cs` | Application |
| `Application/Features/Catalog/Services/IStockUpOrchestrationService.cs` | Application |
| `Application/Features/Catalog/UseCases/GetStockUpOperations/*` | Application |
| `Application/Features/Catalog/UseCases/RetryStockUpOperation/*` | Application |
| `API/Controllers/StockUpOperationsController.cs` | API |
| `frontend/src/pages/StockOperationsPage.tsx` | Frontend |

### Soubory k úpravě

| Soubor | Změna |
|--------|-------|
| `Domain/Features/Catalog/Stock/IEshopStockDomainService.cs` | Přidat `VerifyStockUpExistsAsync` |
| `Adapters/Shoptet/Playwright/ShoptetPlaywrightStockDomainService.cs` | Implementovat verify |
| `Application/Features/Logistics/UseCases/ProcessReceivedBoxes/ProcessReceivedBoxesHandler.cs` | Použít orchestration |
| `Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs` | Použít orchestration |
| `Persistence/ApplicationDbContext.cs` | Přidat DbSet<StockUpOperation> |
| `Persistence/PersistenceModule.cs` | Registrovat repository |
| `Application/ApplicationModule.cs` | Registrovat orchestration service |

---

## Sekvence implementace

```
1. Domain entities      ──┐
2. Persistence          ──┼── Základní infrastruktura
3. Migration            ──┘

4. VerifyStockUpScenario  ── Playwright scenario

5. StockUpOrchestrationService  ── Centrální logika

6. Update ProcessReceivedBoxesHandler    ──┐
7. Update GiftPackageManufactureService  ──┴── Integrace

8. Dashboard (backend + frontend)  ── UI pro monitoring

9. Testy  ── Validace
```

---

## Rizika a mitigace

| Riziko | Pravděpodobnost | Dopad | Mitigace |
|--------|-----------------|-------|----------|
| Shoptet změní UI | Střední | Vysoký | Playwright selektory abstrahovat, dobré error messages |
| Verifikace false negative | Nízká | Střední | Retry mechanismus, manuální review dashboard |
| Performance (2x Playwright per item) | Střední | Nízký | Již async, akceptovatelné |
| DB constraint race condition | Velmi nízká | Nízký | Handled v orchestration service |
