# Systém výpočtu marží - Dokumentace

## 📊 Přehled

Anela Heblo implementuje tříúrovňový systém výpočtu marží (M0-M2) s detailním sledováním nákladů. Každá úroveň postupně přidává další nákladové kategorie pro komplexní analýzu ziskovosti produktů.

## 🏗️ Architektura systému

### Hlavní komponenty

- **MarginCalculationService** - Hlavní služba pro výpočet marží
- **MarginLevel** - Domain objekt reprezentující jednu úroveň marže
- **CostBreakdown** - Rozpis všech nákladových kategorií
- **MonthlyMarginHistory** - Historická data marží po měsících

### Zdroje dat

1. **IMaterialCostRepository** - Náklady na materiály/suroviny
2. **IManufactureCostRepository** - Náklady na výrobu/zpracování
3. **ISalesCostCalculationService** - Náklady na prodej a marketing
4. **IOverheadCostRepository** - Režijní náklady

## 📈 Čtyřúrovňový systém marží

### M0 - Materiálová marže
```
M0 = Prodejní cena - Materiálové náklady
```
**Účel**: Základní marže po odečtení pouze nákladů na suroviny

### M1 - Výrobní marže  
```
M1 = Prodejní cena - (Materiál + Výroba)
```
**Účel**: Marže po odečtení materiálu + zpracování při výrobě

### M2 - Obchodní marže (finální)
```
M2 = Prodejní cena - (Materiál + Výroba + Prodej/Marketing)
```
**Účel**: Finální marže po odečtení všech nákladů včetně marketingu a skladu

## 🆕 Nová struktura MarginLevel

### Vlastnosti

- **`Percentage`** - Marže v procentech `(Marže/Prodejní cena) * 100`
- **`Amount`** - Marže v korunách `Prodejní cena - Náklady`
- **`CostTotal`** - ✨ **NOVÉ**: Celkové kumulativní náklady pro tuto úroveň
- **`CostLevel`** - ✨ **NOVÉ**: Náklady pouze pro aktuální úroveň (bez předchozích)

### Rozdíl mezi CostTotal a CostLevel

#### Příklad s produktem za 200 Kč:

**Nákladové komponenty:**
- Materiál: 50 Kč
- Výroba: 30 Kč
- Prodej/Marketing: 40 Kč

**CostTotal (kumulativní náklady):**
- **M0.CostTotal = 50 Kč** (jen materiál)
- **M1.CostTotal = 80 Kč** (materiál + výroba)
- **M2.CostTotal = 120 Kč** (materiál + výroba + prodej) - finální náklady

**CostLevel (náklady jen pro úroveň):**
- **M0.CostLevel = 50 Kč** (materiálové náklady)
- **M1.CostLevel = 30 Kč** (výrobní náklady)
- **M2.CostLevel = 40 Kč** (prodejní náklady)

**Výsledné marže:**
- **M0**: 200 - 50 = 150 Kč (75%)
- **M1**: 200 - 80 = 120 Kč (60%)
- **M2**: 200 - 120 = 80 Kč (40%) - finální marže

## 💻 Technická implementace

### MarginLevel.Create()

```csharp
// Nová metoda s CostLevel
MarginLevel.Create(sellingPrice: 200, totalCost: 120, levelCost: 40)

// Zpětně kompatibilní metoda
MarginLevel.Create(sellingPrice: 200, totalCost: 120) // CostLevel = 0
```

### Použití v MarginCalculationService

```csharp
return new MonthlyMarginData
{
    Month = month,
    M0 = MarginLevel.Create(sellingPrice, costBreakdown.M0Cost, materialCost),
    M1 = MarginLevel.Create(sellingPrice, costBreakdown.M1Cost, manufacturingCost),
    M2 = MarginLevel.Create(sellingPrice, costBreakdown.M2Cost, salesCost),
    CostsForMonth = costBreakdown
};
```

## 📊 Datová struktura

### MonthlyMarginHistory
```csharp
{
  "MonthlyData": [
    {
      "Month": "2024-01-01T00:00:00",
      "M0": {
        "Percentage": 75.0,
        "Amount": 150.0,
        "CostTotal": 50.0,
        "CostLevel": 50.0
      },
      "M1": {
        "Percentage": 60.0,
        "Amount": 120.0,
        "CostTotal": 80.0,
        "CostLevel": 30.0
      },
      "M2": {
        "Percentage": 40.0,
        "Amount": 80.0,
        "CostTotal": 120.0,
        "CostLevel": 40.0
      }
    }
  ],
  "Averages": {
    "M0": { "Percentage": 72.5, "Amount": 145.0, "CostTotal": 52.5, "CostLevel": 52.5 },
    "M1": { "Percentage": 60.0, "Amount": 120.0, "CostTotal": 80.0, "CostLevel": 30.0 },
    "M2": { "Percentage": 40.0, "Amount": 80.0, "CostTotal": 120.0, "CostLevel": 40.0 }
  }
}
```

## 🔄 Migrace existujícího kódu

### Zpětná kompatibilita

- **`CostBase`** vlastnost je označena jako `[Obsolete]` a mapuje na `CostTotal`
- Starý konstruktor `MarginLevel(percentage, amount, costBase)` je stále funkční
- Stará metoda `Create(sellingPrice, totalCost)` nastavuje `CostLevel = 0`

### Doporučené změny

1. **Přejít na nové vlastnosti:**
   ```csharp
   // Místo
   var totalCost = marginLevel.CostBase;
   
   // Použít
   var totalCost = marginLevel.CostTotal;
   var levelCost = marginLevel.CostLevel;
   ```

2. **Aktualizovat konstruktory:**
   ```csharp
   // Místo
   new MarginLevel(percentage, amount, costBase)
   
   // Použít
   new MarginLevel(percentage, amount, costTotal, costLevel)
   ```

## 🎯 Výhody nového systému

### Pro business analýzu
- **Detailní cost breakdown**: Vidíte přesně, kolik stojí každá komponenta
- **Lepší optimalizace**: Můžete cílit na konkrétní nákladové oblasti
- **Transparentnost**: Jasné rozdělení kumulativních vs. úrovňových nákladů

### Pro reporting
- **Flexibilní vizualizace**: Můžete zobrazit jak celkové náklady, tak jen přírůstky
- **Stackované grafy**: CostLevel umožňuje krásné stackované nákladové grafy
- **Trend analýza**: Sledování změn v jednotlivých nákladových kategoriích

### Pro API klienty
- **Bohatší data**: Frontend dostává více informací pro lepší UX
- **Zpětná kompatibilita**: Existující kód funguje bez změn
- **Progresivní adopce**: Můžete postupně přejít na nové vlastnosti

## 🔍 Použití v UI

### Stackované nákladové grafy
```typescript
// CostLevel data pro stackované sloupcové grafy
const costLevels = [
  { name: 'Materiál', value: marginData.M0.CostLevel },
  { name: 'Výroba', value: marginData.M1.CostLevel },
  { name: 'Prodej', value: marginData.M2.CostLevel }
];
```

### Srovnání celkových vs. přírůstkových nákladů
```typescript
// Celkové náklady (kumulativní) - finální
const totalCosts = marginData.M2.CostTotal;

// Náklady této úrovně (pouze prodej/marketing)
const salesCosts = marginData.M2.CostLevel;
```

## 🏷️ Verze a změny

- **v1.0** - Původní systém s `CostBase`
- **v2.0** - ✨ Nový systém s `CostTotal` a `CostLevel`
- **Zpětná kompatibilita**: Zachována pro hladký přechod

---

*Poslední aktualizace: Leden 2025*