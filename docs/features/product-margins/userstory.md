# Product Margins - User Stories

## Přehled

Modul Product Margins poskytuje komplexní analýzu ziskovosti jednotlivých produktů porovnáním prodejních cen z e-shopu s nákupními náklady z ERP systému. Umožňuje rychlou identifikaci ztrátových produktů a podporuje strategické rozhodování o cenové politice.

## User Stories

### 1. Zobrazení přehledu marží produktů

**Jako** obchodní manažer  
**Chci** vidět přehled marží všech produktů  
**Abych** mohl rychle identifikovat produkty s nízkou nebo zápornou marží

**Akceptační kritéria:**
- Zobrazuje se tabulka se všemi produkty typu "Product" a "Zbozi" (ne materiály ani polotovary)
- Pro každý produkt vidím:
  - Kód produktu
  - Název produktu  
  - Prodejní cenu bez DPH (z e-shopu)
  - Nákupní cenu (z ERP)
  - Vypoctene prumerne naklady
  - Průměrnou marži v %
- Marže jsou barevně odlišené podle výše:
  - Červená: < 30% (kritická)
  - Oranžová: 30-50% (nízká)
  - Žlutá: 50-80% (přijatelná)
  - Zelená: > 80% (dobrá)

### 2. Vyhledávání a filtrování produktů

**Jako** uživatel  
**Chci** filtrovat produkty podle kódu nebo názvu a radit je podle vsech zobrazenych sloupcu  
**Abych** mohl rychle najít konkrétní produkty nebo skupiny produktů

**Akceptační kritéria:**
- Mohu zadat část kódu produktu pro filtrování
- Mohu zadat část názvu produktu pro filtrování
- Filtry se aplikují po stisknutí Enter nebo tlačítka "Filtrovat"
- Tlačítko "Vymazat" odstraní všechny filtry
- Filtrování zachovává barevné označení marží
- Po kliknuti na zahlavi sloupce se podle tohoto sloupce radi

### 3. Stránkování výsledků

**Jako** uživatel  
**Chci** procházet výsledky po stránkách  
**Abych** mohl efektivně pracovat s velkým množstvím produktů

**Akceptační kritéria:**
- Výchozí zobrazení je 20 produktů na stránku
- Mohu změnit počet položek na stránku (10, 20, 50, 100)
- Vidím aktuální rozsah zobrazených položek (např. "1-20 z 150")
- Mohu se pohybovat mezi stránkami pomocí číslovaných tlačítek
- Mohu použít šipky pro předchozí/následující stránku
- Razeni se aplikuje na vsechny produkty, ne pouze na aktualni stranku

## Zdroje dat

### Prodejní cena bez DPH
- **Zdroj**: E-shop (Shoptet) prostřednictvím synchronizace
- **Pole**: `EshopPrice.PriceWithoutVat`
- **Aktualizace**: Při každé synchronizaci s e-shopem

### Nákupní cena
- **Zdroj**: ERP systém (FlexiBee)
- **Pole**: `ErpPrice.PurchasePrice`
- **Aktualizace**: Při synchronizaci s ERP

### Průměrné náklady
- **Aktuální implementace**: Mock data - náhodná variance ±10% od nákupní ceny
- **Budoucí implementace**: Skladova cena z ERP v pripade "Zbozi", novy sloupec "NakladNaVyrobu" z ERP pro "Vyrobky"

## Výpočty marží

### Vzorec pro výpočet marže
```
Marže (%) = ((Prodejní cena - Náklady) / Prodejní cena) × 100
```

### Průměrná marže
- **Vstup**: Prodejní cena s DPH, Průměrné náklady
- **Výpočet**: `((PriceWithVat - AverageCost) / PriceWithVat) × 100`
- **Zaokrouhlení**: Na 2 desetinná místa
- **Null hodnoty**: Pokud chybí cena nebo náklady, zobrazí se "-"

## Formátování dat

### Měna (CZK)
- Bez desetinných míst
- Formát: "1 234 Kč"
- Null hodnoty: "-"

### Procenta
- 2 desetinná místa
- Formát: "25.50%"
- Null hodnoty: "-"

## Oprávnění

- Přístup vyžaduje přihlášení do systému
- Všichni přihlášení uživatelé vidí stejná data
- Budoucí rozšíření: Role-based access control

## Omezení aktuální implementace

1. **Mock data pro náklady** - průměrné náklady a 30denní náklady jsou nyní generovány náhodně
2. **Chybí historie** - není možné zobrazit vývoj marží v čase
3. **Chybí export** - data nelze exportovat do Excel/CSV
4. **Chybí agregace** - není možné zobrazit průměrné marže podle kategorií
5. **Chybí varování** - systém neupozorňuje na kriticky nízké marže

## Budoucí vylepšení

1. **Reálná data nákladů** z historie nákupních objednávek
2. **Grafické zobrazení** trendů marží
3. **Automatická upozornění** na produkty s nízkou marží
4. **Export dat** do různých formátů
5. **Porovnání marží** podle kategorií produktů
6. **What-if analýzy** pro simulaci změn cen