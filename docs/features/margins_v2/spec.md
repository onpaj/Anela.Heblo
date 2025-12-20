# Specifikace výpočtu produktových nákladů a marží (v2)

**Verze:** 2.0
**Datum:** 2025-12-20
**Stav:** Draft

---

## 1. Přehled

Systém sleduje vícenákladové úrovně pro každý produkt s odpovídajícími maržemi. Cílem je poskytnout detailní pohled na nákladovou strukturu a ziskovost produktů s rozlišením mezi různými typy nákladů.

### 1.1 Nákladové úrovně

| Úroveň | Název | Popis |
|--------|-------|-------|
| **M0** | Materiálový náklad | Čistý náklad na materiál/nákupní cena |
| **M1_A** | Plošný výrobní náklad | Rozpočítaný náklad na výrobu (12-měsíční okno) |
| **M1_B** | Přímý výrobní náklad | Měsíční náklad na výrobu dávky |
| **M2** | Skladování + Marketing | Náklady na sklad a marketing |

### 1.2 Datové zdroje

| Úroveň | Interface | Implementace | Zdroj dat |
|--------|-----------|--------------|-----------|
| M0 | `IMaterialCostSource` | `PurchasePriceOnlyMaterialCostSource` | Purchase history / BoM |
| M1_A | `IFlatManufactureCostSource` | `ManufactureCostSource` | `ILedgerService` (VYROBA) |
| M1_B | `IDirectManufactureCostSource` | `DirectManufactureCostSource` | `ILedgerService` (VYROBA) |
| M2 | `ISalesCostSource` | `SalesCostSource` | `ILedgerService` (SKLAD + MARKETING) |

---

## 2. Detailní specifikace nákladových úrovní

### 2.1 M0 - Materiálový náklad

**Účel:** Základní náklad na materiál (suroviny nebo nákupní cena).

**Zdroj dat:**
- `PurchaseHistory` z `CatalogAggregate` pro nakupované produkty
- Receptura (BoM) pro vyráběné produkty

**Algoritmus:**
- **Nakupované produkty:** Průměrná nákupní cena z historie nákupů
- **Vyráběné produkty:** Součet nákladů na materiály dle receptury (BoM)

**Implementace:** `PurchasePriceOnlyMaterialCostSource.cs`

---

### 2.2 M1_A - Plošný výrobní náklad

**Účel:** Plošně rozpočítaný náklad na výrobu produktu. Využívá abstraktní metriku `ManufactureDifficulty` k fair distribuci nákladů mezi produkty s různou náročností výroby.

**Časové okno:** 12 měsíců (klouzavé okno)

**Zdroj dat:**
- **Náklady:** `ILedgerService.GetDirectCosts(dateFrom, dateTo, department: "VYROBA")`
- **Výroba:** `ManufactureHistory` z `CatalogAggregate`
- **Náročnost:** `ManufactureDifficulty` (historická hodnota platná v daném měsíci)

**Algoritmus:**

```
Vstup:
- dateFrom, dateTo (12-měsíční okno)
- productCode (produkt, pro který počítáme náklad)

Krok 1: Načíst celkové výrobní náklady za období
  totalCosts = Σ ILedgerService.GetDirectCosts(dateFrom, dateTo, department: "VYROBA")

Krok 2: Načíst výrobní historii všech produktů
  allManufactureHistory = IManufactureHistoryClient.GetHistoryAsync(dateFrom, dateTo, null)

Krok 3: Spočítat celkové vážené výrobní body
  totalWeightedPoints = 0
  pro každý záznam v allManufactureHistory:
    difficulty = GetHistoricalDifficulty(productCode, recordDate)
    totalWeightedPoints += recordAmount × difficulty

Krok 4: Vypočítat cenu jednoho výrobního bodu
  costPerPoint = totalCosts / totalWeightedPoints

Krok 5: Náklad M1_A pro daný produkt (průměr za období)
  productDifficulty = GetHistoricalDifficulty(productCode, currentDate)
  M1_A = costPerPoint × productDifficulty
```

**Poznámky:**
- `GetHistoricalDifficulty()` vrací hodnotu `ManufactureDifficulty` platnou v daném datu z `ManufactureDifficultySettings`
- Pokud produkt nemá definovanou `ManufactureDifficulty`, použije se výchozí hodnota (konstanta)

**Implementace:** `ManufactureCostSource.cs`

---

### 2.3 M1_B - Přímý výrobní náklad (měsíční)

**Účel:** Přímé náklady na výrobní dávku rozpočítané měsíčně. Odráží skutečné náklady v měsíci, kdy k výrobě došlo.

**Charakteristika:**
- Výroba nemusí probíhat každý měsíc → mohou existovat měsíce s nulovým nákladem
- Náklad je vázán na konkrétní měsíc výroby

**Zdroj dat:**
- **Náklady:** `ILedgerService.GetDirectCosts(month, department: "VYROBA")` pro konkrétní měsíc
- **Výroba:** `ManufactureHistory` z `CatalogAggregate` pro daný měsíc
- **Náročnost:** `ManufactureDifficulty` (historická hodnota platná v daném měsíci)

**Algoritmus (pro každý měsíc samostatně):**

```
Vstup:
- month (měsíc, pro který počítáme)
- productCode (produkt, pro který počítáme náklad)

Krok 1: Načíst náklady na výrobu za měsíc
  monthlyCosts = ILedgerService.GetDirectCosts(month, department: "VYROBA")

Krok 2: Načíst výrobní historii všech produktů za měsíc
  monthlyProduction = allProductsManufactureHistory.Where(m => m.Month == month)

Krok 3: Pokud daný produkt nebyl v měsíci vyroben
  if !monthlyProduction.Any(m => m.ProductCode == productCode):
    M1_B[month] = 0  // nebo null

Krok 4: Spočítat celkové vážené výrobní body za měsíc
  monthlyWeightedPoints = 0
  pro každý záznam v monthlyProduction:
    difficulty = GetHistoricalDifficulty(productCode, recordDate)
    monthlyWeightedPoints += recordAmount × difficulty

Krok 5: Vypočítat podíl produktu na výrobě
  productProduction = monthlyProduction.Where(m => m.ProductCode == productCode).Sum(amount)
  productDifficulty = GetHistoricalDifficulty(productCode, month)
  productWeightedPoints = productProduction × productDifficulty

Krok 6: Rozpočítat náklady na produkt
  M1_B[month] = monthlyCosts × (productWeightedPoints / monthlyWeightedPoints)
```

**Poznámky:**
- Výstup je časová řada: `Dictionary<DateTime, decimal>` (měsíc → náklad)
- Měsíce bez výroby mají M1_B = 0

**Implementace:** `DirectManufactureCostSource.cs` (nový soubor)

---

### 2.4 M2 - Skladování a Marketing

**Účel:** Náklady na skladování a marketing alokované podle podílu na celkovém prodeji.

**Zdroj dat:**
- **Náklady:**
  - `ILedgerService.GetDirectCosts(dateFrom, dateTo, department: "SKLAD")`
  - `ILedgerService.GetDirectCosts(dateFrom, dateTo, department: "MARKETING")`
- **Prodeje:** `SalesHistory` z `CatalogAggregate`

**Algoritmus:**

```
Vstup:
- dateFrom, dateTo (období)
- productCode (produkt, pro který počítáme náklad)

Krok 1: Načíst náklady za období
  warehouseCosts = ILedgerService.GetDirectCosts(dateFrom, dateTo, department: "SKLAD")
  marketingCosts = ILedgerService.GetDirectCosts(dateFrom, dateTo, department: "MARKETING")
  totalCosts = warehouseCosts + marketingCosts

Krok 2: Spočítat celkový objem prodeje (všechny produkty)
  allProducts = ICatalogRepository.GetAllAsync()
  totalSales = 0
  pro každý produkt v allProducts:
    productSales = produkt.SalesHistory
      .Where(s => s.Date >= dateFrom && s.Date <= dateTo)
      .Sum(s => s.SumB2B + s.SumB2C)
    totalSales += productSales

Krok 3: Spočítat prodej daného produktu
  productSales = CatalogAggregate[productCode].SalesHistory
    .Where(s => s.Date >= dateFrom && s.Date <= dateTo)
    .Sum(s => s.SumB2B + s.SumB2C)

Krok 4: Rozpočítat náklady na produkt
  if totalSales > 0:
    M2 = totalCosts × (productSales / totalSales)
  else:
    M2 = 0
```

**Poznámky:**
- Produkty bez prodeje mají M2 = 0
- Alokace je proporcionální k objemu prodeje (v Kč)

**Implementace:** `SalesCostSource.cs` (nový soubor)

---

## 3. Výpočet marží

### 3.1 Struktura MarginLevel

```csharp
public class MarginLevel
{
    public decimal Percentage { get; }     // Marže v %
    public decimal Amount { get; }         // Marže v Kč
    public decimal CostTotal { get; }      // Kumulativní náklad do této úrovně
    public decimal CostLevel { get; }      // Náklad pouze této úrovně
}
```

### 3.2 Výpočet MarginLevel

```csharp
MarginLevel.Create(sellingPrice, totalCost, levelCost)

kde:
- sellingPrice = prodejní cena bez DPH
- totalCost = kumulativní náklad od M0 po aktuální úroveň
- levelCost = náklad pouze aktuální úrovně
```

**Vzorec:**
```
marginAmount = sellingPrice - totalCost
marginPercentage = (marginAmount / sellingPrice) × 100
```

### 3.3 Příklad výpočtu

```
SellingPrice = 100 Kč (bez DPH)

Náklady:
- M0_cost = 30 Kč (materiál)
- M1_A_cost = 15 Kč (plošná výroba)
- M1_B_cost = 5 Kč (přímá výroba)
- M2_cost = 10 Kč (sklad + marketing)

Marže:
- M0:    totalCost = 30,                margin = (100 - 30) / 100 = 70%
- M1_A:  totalCost = 30 + 15 = 45,      margin = (100 - 45) / 100 = 55%
- M1_B:  totalCost = 30 + 5 = 35,       margin = (100 - 35) / 100 = 65%
- M2:    totalCost = 30 + 15 + 5 + 10 = 60,  margin = (100 - 60) / 100 = 40%
```

**Poznámka:** M1_A a M1_B jsou nezávislé úrovně (ne sekvenční).

---

## 4. Datové struktury

### 4.1 MarginData

```csharp
public class MarginData
{
    public MarginLevel M0 { get; init; } = MarginLevel.Zero;     // Materiál
    public MarginLevel M1_A { get; init; } = MarginLevel.Zero;   // Plošná výroba
    public MarginLevel M1_B { get; init; } = MarginLevel.Zero;   // Přímá výroba
    public MarginLevel M2 { get; init; } = MarginLevel.Zero;     // Sklad + Marketing
}
```

**Změna oproti v1:** Struktura se mění z `M0, M1, M2, M3` na `M0, M1_A, M1_B, M2`.

### 4.2 MonthlyMarginHistory

```csharp
public class MonthlyMarginHistory
{
    public Dictionary<DateTime, MarginData> MonthlyData { get; set; } = new();
    public DateTime LastUpdated { get; set; }
}
```

**Účel:** Uchovává historii marží po měsících. Klíč = první den měsíce.

### 4.3 MonthlyCost (výstup cost sources)

```csharp
public class MonthlyCost
{
    public DateTime Month { get; }
    public decimal Cost { get; }
}
```

---

## 5. Architektura

### 5.1 Cost Source Pattern

Všechny cost sources implementují `ICostDataSource`:

```csharp
public interface ICostDataSource
{
    Task<Dictionary<string, List<MonthlyCost>>> GetCostsAsync(
        List<string>? productCodes = null,
        DateOnly? dateFrom = null,
        DateOnly? dateTo = null,
        CancellationToken cancellationToken = default);
}
```

**Výstup:** `Dictionary<string, List<MonthlyCost>>`
- Klíč = `productCode`
- Hodnota = seznam měsíčních nákladů

### 5.2 MarginCalculationService

Orchestrátor výpočtu marží:

```csharp
public class MarginCalculationService : IMarginCalculationService
{
    private readonly IMaterialCostSource _materialCostSource;           // M0
    private readonly IFlatManufactureCostSource _flatManufactureCostSource;   // M1_A
    private readonly IDirectManufactureCostSource _directManufactureCostSource; // M1_B
    private readonly ISalesCostSource _salesCostSource;                 // M2

    public async Task<MonthlyMarginHistory> GetMarginAsync(
        CatalogAggregate product,
        DateOnly dateFrom,
        DateOnly dateTo,
        CancellationToken cancellationToken = default)
    {
        // 1. Načíst data ze všech cost sources
        // 2. Kombinovat náklady po měsících
        // 3. Vypočítat MarginLevel pro každou úroveň
        // 4. Sestavit MonthlyMarginHistory
    }
}
```

### 5.3 Dependency Injection

```csharp
// CatalogModule.cs
services.AddTransient<IMaterialCostSource, PurchasePriceOnlyMaterialCostSource>();
services.AddTransient<IFlatManufactureCostSource, ManufactureCostSource>();
services.AddTransient<IDirectManufactureCostSource, DirectManufactureCostSource>();
services.AddTransient<ISalesCostSource, SalesCostSource>();
services.AddTransient<IMarginCalculationService, MarginCalculationService>();
```

---

## 6. Implementační požadavky

### 6.1 Klíčová rozhodnutí

| Téma | Rozhodnutí |
|------|------------|
| M2 alokace | Podle objemu prodeje (SumB2B + SumB2C) |
| MarginData struktura | Rozšířit na M0, M1_A, M1_B, M2 |
| ManufactureDifficulty | Použít historickou hodnotu platnou v měsíci výroby |
| M1_A okno | 12 měsíců (klouzavé) |
| M1_B granularita | Po měsících |

### 6.2 Soubory k vytvoření/úpravě

**Domain Layer:**
- `MarginData.cs` - rozšířit strukturu
- `MonthlyMarginHistory.cs` - implementovat

**Application Layer - Cost Sources:**
- `ManufactureCostSource.cs` - doimplementovat M1_A
- `DirectManufactureCostSource.cs` - **nový soubor** pro M1_B
- `SalesCostSource.cs` - **nový soubor** pro M2
- `PurchasePriceOnlyMaterialCostSource.cs` - ověřit/doplnit

**Application Layer - Services:**
- `MarginCalculationService.cs` - implementovat `CalculateMarginHistoryFromData`

**DI:**
- `CatalogModule.cs` - registrace nových cost sources

### 6.3 Závislosti

- `ILedgerService` - existující, funkční (poskytuje náklady dle oddělení)
- `IManufactureHistoryClient` - existující, poskytuje výrobní historii
- `ICatalogRepository` - existující, poskytuje produktové katalogy
- `ManufactureDifficultySettings` - existující, podporuje historii hodnot

---

## 7. Testování

### 7.1 Unit testy

**Cost Sources:**
- Test výpočtu M1_A při známých vstupních datech
- Test výpočtu M1_B pro měsíce s/bez výroby
- Test M2 alokace podle prodejů
- Test okrajových případů (nulové náklady, žádná výroba, žádný prodej)

**MarginCalculationService:**
- Test kombinování dat ze všech sources
- Test generování MonthlyMarginHistory
- Test výpočtu MarginLevel

### 7.2 Integrační testy

- Test s reálnými daty z `ILedgerService`
- Test s historickou `ManufactureDifficulty`
- Test konzistence výpočtů napříč měsíci

---

## 8. Přílohy

### 8.1 Cesty souborů

```
backend/src/Anela.Heblo.Domain/Features/Catalog/
├── MarginData.cs
├── MarginLevel.cs
├── MonthlyMarginHistory.cs
├── Repositories/
│   ├── ICostDataSource.cs
│   ├── IMaterialCostSource.cs
│   ├── IFlatManufactureCostSource.cs
│   ├── IDirectManufactureCostSource.cs
│   └── ISalesCostSource.cs
└── Services/
    └── IMarginCalculationService.cs

backend/src/Anela.Heblo.Application/Features/Catalog/
├── Repositories/
│   ├── PurchasePriceOnlyMaterialCostSource.cs
│   ├── ManufactureCostSource.cs
│   ├── DirectManufactureCostSource.cs (nový)
│   └── SalesCostSource.cs (nový)
├── Services/
│   └── MarginCalculationService.cs
└── CatalogModule.cs
```

### 8.2 Další služby

- `ILedgerService` - `/backend/src/Anela.Heblo.Domain/Accounting/Ledger/ILedgerService.cs`
- `IManufactureHistoryClient` - `/backend/src/Anela.Heblo.Domain/Features/Manufacture/IManufactureHistoryClient.cs`
- `ManufactureDifficultyConfiguration` - `/backend/src/Anela.Heblo.Domain/Features/Catalog/ManufactureDifficultyConfiguration.cs`
