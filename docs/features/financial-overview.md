# Financial Overview - User Story

## Overview
Finanční přehled firmy zobrazující příjmy, náklady a celkovou bilanci za posledních 26 měsíců. Implementace ve dvou fázích - základní finanční přehled a rozšíření o skladové hospodaření.

## User Story
**Jako** manažer firmy  
**chci** vidět finanční přehled za posledních 26 měsíců včetně vlivu skladového hospodaření  
**abych** mohl analyzovat finanční vývoj a trendy společnosti včetně hodnoty zásob.

## Fáze 1: Základní finanční přehled

### Acceptance Criteria - Fáze 1

#### Primární zobrazení - Graf
- [ ] Graf zobrazuje data za posledních 26 měsíců (včetně aktuálního)
- [ ] Graf obsahuje tři datové řady:
  - **Příjmy**: Všechny položky z LedgerService s debit account prefix "6"
  - **Náklady**: Všechny položky z LedgerService s debit account prefix "5"  
  - **Finanční bilance**: Vypočítaná hodnota (Příjmy - Náklady)
- [ ] Osa X: Měsíce (MM/YYYY formát)
- [ ] Osa Y: Částky v CZK
- [ ] Graf je interaktivní s možností zobrazení konkrétních hodnot při hover
- [ ] Vizuální rozlišení pozitivní/negativní bilance (barvy)

#### Sekundární zobrazení - Tabulka
- [ ] Tabulka pod grafem s konkrétními hodnotami
- [ ] Sloupce: Měsíc, Příjmy, Náklady, Finanční bilance
- [ ] Řazení od nejnovějšího měsíce
- [ ] Číselné formátování v CZK s tisícovými oddělovači
- [ ] Barevné zvýraznění pozitivní/negativní bilance

#### Technické požadavky - Fáze 1
- [ ] Data načítána z LedgerService
- [ ] Filtrování podle debit account prefixů:
  - Příjmy: prefix "6"
  - Náklady: prefix "5"
- [ ] Agregace dat po měsících
- [ ] Responsivní design pro různé velikosti obrazovek
- [ ] Loading states během načítání dat
- [ ] Error handling pro případy nedostupnosti dat
- [ ] RBAC autorizace - vyžadován claim "FinancialOverview.View" pro přístup k view
- [ ] Při absenci požadovaného claim zobrazit 403 Forbidden s informací o chybějícím oprávnění

#### Data Requirements - Fáze 1
- [ ] Backend endpoint pro získání finančních dat za zadané období
- [ ] Agregace LedgerService dat podle měsíců
- [ ] Filtrování podle debit account prefixů
- [ ] Formát response: měsíc, celkové příjmy, celkové náklady za měsíc

## Fáze 2: Rozšíření o skladové hospodaření

### Acceptance Criteria - Fáze 2

#### Rozšíření grafu
- [ ] Graf rozšířen o dodatečné datové řady:
  - **Změna hodnoty skladu**: Měsíční změna hodnoty zásob (může být + nebo -)
  - **Celková bilance**: Finanční bilance + Změna hodnoty skladu
- [ ] Možnost zapnout/vypnout zobrazení skladových dat v grafu
- [ ] Legenda grafu rozšířena o nové datové řady
- [ ] Vizuální odlišení finanční bilance od celkové bilance

#### Rozšíření tabulky
- [ ] Přidání sloupců: Změna hodnoty skladu, Celková bilance firmy
- [ ] Tooltips vysvětlující význam jednotlivých sloupců
- [ ] Možnost skrýt/zobrazit skladové sloupce

#### Technické požadavky - Fáze 2
- [ ] Využití existujícího StockToDate service pro výpočet hodnoty skladu
- [ ] Výpočet měsíční změny hodnoty skladu:
  - Načtení stavu skladu k 1. dni každého měsíce pomocí StockToDate
  - Výpočet rozdílu oproti předchozímu měsíci (StockValue[currentMonth] - StockValue[previousMonth])
  - Sledování tří hlavních skladů: ZBOZI, MATERIAL, POLOTOVARY
- [ ] Backend: Uchovávání dat po měsících a po konkrétních skladech (rozložení dle typu)
- [ ] Frontend: Zobrazení jako součet změn hodnoty všech tří skladů
- [ ] Optimalizace výkonu pro větší objem dat

#### Data Requirements - Fáze 2
- [ ] Rozšíření backend endpointu o skladová data ze StockToDate service
- [ ] Získání historických hodnot skladů k 1. dni každého měsíce
- [ ] Výpočet měsíčních změn hodnoty pro každý sklad (ZBOZI, MATERIAL, POLOTOVARY)
- [ ] Backend response: změna hodnoty skladu po typech + celková změna
- [ ] Frontend agregace: zobrazení celkové změny hodnoty skladu (součet všech tří typů)

## Technical Notes
- Využití existujícího LedgerService z accounting modulu
- Integrace s grafovou knihovnou (Chart.js nebo podobnou)
- RBAC autorizace pomocí claim "FinancialOverview.View"
- Dodržení design systému aplikace
- Responsivní layout podle layout guidelines

## Future Enhancements
- Možnost změny časového období (6/12/36 měsíců)
- Export dat do Excel/PDF
- Drill-down funkcionalita pro detailní analýzu
- Porovnání s předchozím obdobím
- Predikce trendů