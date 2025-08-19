# Katalog – Zobrazení marží produktů

Tento dokument popisuje feature pro zobrazení a analýzu marží produktů v rámci Katalogu. Feature umožňuje sledování ziskovosti jednotlivých produktů na základě prodejních cen z e-shopu a nákladů z výrobních kalkulací.

---

## 1. Přehled feature

**Účel**: Poskytuje přehled ziskovosti produktů prostřednictvím výpočtu marží na základě porovnání prodejních cen a výrobních nákladů.

**Hlavní funkcionalita**:
- Zobrazení marže v procentech a korunách
- Zobrazení v detailu katalogové položky
- Samostatná stránka pro přehled marží všech produktů
- Barevné rozlišení podle výše marže
- Filtrování a stránkování

---

## 2. Zdroje dat

### 2.1 Prodejní cena
**Zdroj**: E-shop (Shoptet)
- **Pole**: `EshopPrice.PriceWithoutVat`
- **Popis**: Prodejní cena bez DPH, za kterou se produkt prodává zákazníkům
- **Integrace**: Automaticky synchronizována z e-shopu prostřednictvím ShoptetPriceClient

### 2.2 Výrobní náklady
**Zdroj**: Historie výrobních nákladů (ManufactureCostHistory)
- **Pole**: `ManufactureCostHistory.Total`
- **Popis**: Celkové náklady na výrobu jedné jednotky produktu včetně:
  - Náklady na materiály
  - Mzdové náklady
  - Režijní náklady
- **Výpočet**: Průměr z historie výrobních nákladů pro daný produkt
- **Integrace**: Kalkulace probíhá při výrobních dávkách prostřednictvím ManufactureCostCalculationService

---

## 3. Výpočet marže

### 3.1 Vzorce
**Marže v korunách**:
```
MarginAmount = ProdejníCena - PrůměrnéVýrobníNáklady
```

**Marže v procentech**:
```
MarginPercentage = (MarginAmount / ProdejníCena) × 100
```

### 3.2 Příklad výpočtu
- Prodejní cena (bez DPH): 1 000 Kč
- Průměrné výrobní náklady: 600 Kč
- **Marže v Kč**: 1 000 - 600 = 400 Kč
- **Marže v %**: (400 / 1 000) × 100 = 40 %

### 3.3 Speciální případy
**Chybějící data**:
- Pokud chybí historie výrobních nákladů → marže = 0
- Pokud je prodejní cena ≤ 0 → marže = 0
- Pokud jsou výrobní náklady ≤ 0 → marže = 0

**Záporné marže**:
- Vznikají při výrobních nákladech vyšších než prodejní cena
- Indikují ztrátové produkty vyžadující pozornost

---

## 4. Zobrazení v UI

### 4.1 Detail katalogové položky - záložka "Marže"
**Umístění**: `CatalogDetail` → záložka "Marže"
**Obsah**:
- Grafické zobrazení historie výrobních nákladů (spojnicový graf)
- Souhrn nákladů s barevným zvýrazněním
- Aktuální marže v % i Kč s barevným kódováním

**Barevné kódování marže**:

*Detail katalogové položky (záložka Marže):*
- 🟢 **Zelená**: Kladná marže (zisk)
- 🔴 **Červená**: Záporná marže (ztráta)

*Seznam produktových marží:*
- 🟢 **Zelená (≥ 80%)**: Vynikající marže
- 🟡 **Žlutá (50-79%)**: Dobrá marže
- 🟠 **Oranžová (30-49%)**: Přijatelná marže
- 🔴 **Červená (< 30%)**: Nízká marže
- 🔘 **Šedá**: Chybějící nebo nevalidní data

### 4.2 Seznam produktových marží
**Umístění**: Samostatná stránka `/product-margins`
**Funkcionalita**:
- Tabulkový přehled všech produktů s maržemi
- Filtrování podle kódu nebo názvu produktu
- Řazení podle různých kritérií (marže, název, kód)
- Stránkování s konfigurovatelnou velikostí stránky
- Barevné rozlišení řádků podle výše marže

**Zobrazované sloupce**:
- Kód produktu
- Název produktu
- Prodejní cena (s DPH/bez DPH)
- Výrobní náklady
- Marže v %
- Marže v Kč

---

## 5. Technická implementace

### 5.1 Backend - Domain Model
**Soubor**: `CatalogAggregate.cs`
**Klíčové vlastnosti**:
```csharp
public decimal MarginPercentage { get; set; } // Marže v procentech
public decimal MarginAmount { get; set; }     // Marže v korunách
```

**Metoda výpočtu**:
```csharp
public void UpdateMarginCalculation()
{
    var averageTotalCost = ManufactureCostHistory.Average(record => record.Total);
    var sellingPrice = EshopPrice?.PriceWithoutVat ?? 0;
    
    if (sellingPrice > 0 && averageTotalCost > 0)
    {
        MarginAmount = sellingPrice - averageTotalCost;
        MarginPercentage = (MarginAmount / sellingPrice) * 100;
    }
    else
    {
        MarginPercentage = 0;
        MarginAmount = 0;
    }
}
```

### 5.2 Backend - API Endpoint
**Controller**: `ProductMarginsController.cs`
**Endpoint**: `GET /api/productmargins`
**Handler**: `GetProductMarginsHandler.cs`

**Query parametry**:
- `productCode`: Filtr podle kódu produktu
- `productName`: Filtr podle názvu produktu
- `pageNumber`: Číslo stránky
- `pageSize`: Velikost stránky
- `sortBy`: Kritérium řazení
- `sortDescending`: Směr řazení

### 5.3 Frontend - React komponenty
**Detail**: `CatalogDetail.tsx` → `MarginsTab`
**Seznam**: `ProductMarginsList.tsx`
**API Hook**: `useProductMargins.ts`

**Klíčové funkce**:
- TanStack Query pro cache management
- Debounced vyhledávání (na Enter)
- Responsive design
- Accessibility (keyboard navigation)

---

## 6. Obchodní hodnota

### 6.1 Klíčové přínosy
1. **Transparentnost ziskovosti**: Jasný přehled o ziskovosti jednotlivých produktů
2. **Identifikace problémů**: Rychlé odhalení ztrátových nebo málo ziskových produktů
3. **Cenová optimalizace**: Podklad pro rozhodování o úpravách prodejních cen
4. **Portfolio management**: Pomoc při rozhodování o pokračování/ukončení výroby produktů
5. **Nákladová kontrola**: Sledování vývoje výrobních nákladů a jejich dopadu na marže

### 6.2 Typické použití
- **Pravidelné review**: Měsíční/čtvrtletní analýza portfolia produktů
- **Cenová strategie**: Nastavování prodejních cen na základě požadované marže
- **Optimalizace výroby**: Identifikace produktů s nejlepšími marže­mi pro prioritní výrobu
- **Kontrola nákladů**: Sledování vývoje výrobních nákladů v čase

---

## 7. Omezení a budoucí rozšíření

### 7.1 Současná omezení
- Marže se počítá pouze pro produkty s historií výrobních nákladů
- Nerozlišuje se mezi různými typy nákladů (materiál vs. mzdy vs. režie)
- Nesledují se marže v čase (historický vývoj)

### 7.2 Plánovaná rozšíření
1. **Historické trendy**: Grafické zobrazení vývoje marží v čase
2. **Detailní analýza nákladů**: Rozpad marže na jednotlivé složky nákladů
3. **Srovnání s konkurencí**: Import a porovnání s tržními cenami
4. **Alerting**: Automatické upozornění při poklesu marže pod stanovenou hranici
5. **Export dat**: Možnost exportu analýzy marží do Excel/CSV

### 7.3 Integrace s dalšími moduly
- **Purchase modul**: Vliv nákupních cen materiálů na výsledné marže
- **Sales modul**: Korelace mezi marží a objemem prodeje
- **Forecast modul**: Predikce budoucích marží na základě plánovaných změn

---

## 8. Metriky a KPI

### 8.1 Sledované metriky
- **Průměrná marže portfolia**: Celková ziskovost produktového portfolia
- **Počet ztrátových produktů**: Produkty s negativní marží
- **Distribuce marží**: Rozložení produktů podle pásem ziskovosti
- **Trend vývoje marží**: Měsíční/čtvrtletní změny

### 8.2 Obchodní KPI
- **Cílová průměrná marže**: > 25%
- **Maximální podíl ztrátových produktů**: < 5%
- **Minimální marže pro nové produkty**: > 20%

---

## 9. Testovací scénáře

### 9.1 Základní funkcionalita
1. **Správný výpočet marže**: Ověření matematické správnosti výpočtu
2. **Barevné kódování**: Kontrola správného přiřazení barev podle marže
3. **Filtrování**: Test vyhledávání podle kódu a názvu produktu
4. **Stránkování**: Ověření funkčnosti navigace mezi stránkami

### 9.2 Speciální případy
1. **Chybějící data**: Chování při absenci výrobních nákladů nebo prodejní ceny
2. **Záporné marže**: Správné zobrazení ztrátových produktů
3. **Nulové hodnoty**: Ošetření okrajových případů s nulovými hodnotami

### 9.3 Performance testy
1. **Velké množství dat**: Testování s tisíci produktů
2. **Rychlost odezvy**: Kontrola doby načítání stránky
3. **Responzivita**: Test na různých velikostech obrazovek

---

Tato dokumentace poskytuje komplexní přehled o implementaci a fungování feature pro zobrazení marží produktů v systému Anela Heblo.