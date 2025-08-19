# Product Margins - Test Scenarios

## 1. Zobrazení dat

### Test 1.1: Základní zobrazení tabulky marží
**Předpoklady:**
- Uživatel je přihlášen
- V systému existují produkty s cenami

**Kroky:**
1. Navigovat na stránku "Marže produktů"
2. Ověřit zobrazení tabulky

**Očekávaný výsledek:**
- Zobrazí se tabulka s 8 sloupci (kód, název, cena s DPH, nákupní cena, náklad průměr, náklad 30 dní, marže průměr, marže 30 dní)
- Data jsou zobrazena pro produkty (ne materiály)
- Výchozí stránkování je 20 položek

### Test 1.2: Barevné označení marží
**Předpoklady:**
- Existují produkty s různými hodnotami marží

**Kroky:**
1. Zobrazit tabulku marží
2. Zkontrolovat barevné označení sloupců marží

**Očekávaný výsledek:**
- Marže < 10%: červená barva
- Marže 10-20%: oranžová barva
- Marže 20-30%: žlutá barva
- Marže > 30%: zelená barva

### Test 1.3: Zobrazení chybějících dat
**Předpoklady:**
- Některé produkty nemají cenu nebo náklady

**Kroky:**
1. Zobrazit produkty s chybějícími daty

**Očekávaný výsledek:**
- Pro chybějící hodnoty se zobrazí "-"
- Marže se nezobrazí (také "-")
- Aplikace nespadne

## 2. Filtrování

### Test 2.1: Filtrování podle kódu produktu
**Kroky:**
1. Zadat část kódu produktu (např. "ABC")
2. Stisknout Enter nebo tlačítko "Filtrovat"

**Očekávaný výsledek:**
- Zobrazí se pouze produkty obsahující "ABC" v kódu
- Počet výsledků se aktualizuje
- Stránkování se resetuje na první stránku

### Test 2.2: Filtrování podle názvu produktu
**Kroky:**
1. Zadat část názvu produktu (např. "krém")
2. Stisknout Enter nebo tlačítko "Filtrovat"

**Očekávaný výsledek:**
- Zobrazí se pouze produkty obsahující "krém" v názvu (case-insensitive)
- Funguje s diakritikou

### Test 2.3: Kombinované filtrování
**Kroky:**
1. Zadat kód produktu "ABC"
2. Zadat název "krém"
3. Aplikovat filtry

**Očekávaný výsledek:**
- Zobrazí se produkty vyhovující OBA filtry současně
- Prázdný výsledek, pokud žádný produkt nevyhovuje

### Test 2.4: Vymazání filtrů
**Kroky:**
1. Aplikovat nějaké filtry
2. Kliknout na tlačítko "Vymazat"

**Očekávaný výsledek:**
- Všechny filtry se vymažou
- Zobrazí se všechny produkty
- Stránkování se resetuje na první stránku

### Test 2.5: Filtrování s prázdnými hodnotami
**Kroky:**
1. Nechat filtry prázdné
2. Kliknout "Filtrovat"

**Očekávaný výsledek:**
- Zobrazí se všechny produkty
- Žádná chyba

## 3. Stránkování

### Test 3.1: Navigace mezi stránkami
**Kroky:**
1. Kliknout na číslo stránky 2
2. Použít šipku "Další"
3. Použít šipku "Předchozí"

**Očekávaný výsledek:**
- Správné přepínání mezi stránkami
- Aktualizace zobrazených dat
- Aktualizace informace o rozsahu (např. "21-40 z 150")

### Test 3.2: Změna počtu položek na stránku
**Kroky:**
1. Změnit počet položek z 20 na 50

**Očekávaný výsledek:**
- Zobrazí se 50 položek
- Počet stránek se přepočítá
- Vrátí se na první stránku

### Test 3.3: Stránkování s filtry
**Kroky:**
1. Aplikovat filtr
2. Přejít na druhou stránku
3. Změnit filtr

**Očekávaný výsledek:**
- Po změně filtru se vrátí na první stránku
- Stránkování odpovídá filtrovaným výsledkům

## 4. Výpočty marží

### Test 4.1: Správný výpočet marže
**Testovací data:**
- Prodejní cena: 1000 Kč
- Náklady: 600 Kč

**Očekávaný výpočet:**
- Marže = ((1000 - 600) / 1000) × 100 = 40%

### Test 4.2: Marže při nulové prodejní ceně
**Testovací data:**
- Prodejní cena: 0 Kč
- Náklady: 100 Kč

**Očekávaný výsledek:**
- Marže se nezobrazí (hodnota "-")
- Žádná chyba nebo dělení nulou

### Test 4.3: Marže při nulových nákladech
**Testovací data:**
- Prodejní cena: 1000 Kč
- Náklady: 0 Kč

**Očekávaný výsledek:**
- Marže = 100%

### Test 4.4: Záporná marže
**Testovací data:**
- Prodejní cena: 100 Kč
- Náklady: 150 Kč

**Očekávaný výsledek:**
- Marže = -50%
- Zobrazí se červeně

## 5. Výkon a odezva

### Test 5.1: Rychlost načítání
**Kroky:**
1. Otevřít stránku s maržemi

**Očekávaný výsledek:**
- Data se načtou do 2 sekund
- Zobrazí se loading indikátor během načítání

### Test 5.2: Velké množství dat
**Předpoklady:**
- V systému je > 1000 produktů

**Kroky:**
1. Zobrazit seznam produktů
2. Změnit stránkování na 100 položek

**Očekávaný výsledek:**
- Stránka zůstává responzivní
- Scrollování je plynulé

### Test 5.3: Cache a aktualizace
**Kroky:**
1. Zobrazit marže
2. Přejít na jinou stránku
3. Vrátit se zpět

**Očekávaný výsledek:**
- Data se načtou z cache (rychleji)
- Po 5 minutách se data obnoví

## 6. Chybové stavy

### Test 6.1: Chyba při načítání dat
**Simulace:**
- API vrátí chybu 500

**Očekávaný výsledek:**
- Zobrazí se chybová hláška
- Uživatel je informován o problému

### Test 6.2: Timeout požadavku
**Simulace:**
- API neodpoví do 30 sekund

**Očekávaný výsledek:**
- Zobrazí se timeout chyba
- Možnost zkusit znovu

### Test 6.3: Neplatná data
**Simulace:**
- API vrátí neplatný formát dat

**Očekávaný výsledek:**
- Aplikace nespadne
- Zobrazí se obecná chybová hláška

## 7. Responsivní design

### Test 7.1: Zobrazení na mobilu
**Kroky:**
1. Otevřít stránku na mobilním zařízení

**Očekávaný výsledek:**
- Tabulka je horizontálně scrollovatelná
- Filtry jsou dostupné
- Stránkování funguje dotykem

### Test 7.2: Zobrazení na tabletu
**Kroky:**
1. Otevřít na tabletu

**Očekávaný výsledek:**
- Optimální rozložení pro tablet
- Všechny funkce dostupné

## 8. Přístupová práva

### Test 8.1: Nepřihlášený uživatel
**Kroky:**
1. Pokusit se zobrazit marže bez přihlášení

**Očekávaný výsledek:**
- Přesměrování na login
- Po přihlášení návrat na marže

### Test 8.2: Přihlášený uživatel
**Kroky:**
1. Přihlásit se
2. Navigovat na marže

**Očekávaný výsledek:**
- Úspěšné zobrazení dat
- Všechny funkce dostupné

## 9. Integrace s ostatními moduly

### Test 9.1: Aktualizace cen z ERP
**Kroky:**
1. Změnit cenu v ERP
2. Spustit synchronizaci
3. Zobrazit marže

**Očekávaný výsledek:**
- Nová cena se projeví v marži
- Přepočet je správný

### Test 9.2: Aktualizace cen z e-shopu
**Kroky:**
1. Změnit cenu v e-shopu
2. Synchronizovat
3. Zkontrolovat marže

**Očekávaný výsledek:**
- Aktualizovaná prodejní cena
- Správný přepočet marže

## 10. Datová konzistence

### Test 10.1: Pouze produkty v seznamu
**Ověření:**
- V tabulce se nezobrazují materiály
- V tabulce se nezobrazují polotovary

**Očekávaný výsledek:**
- Pouze položky typu "Product"

### Test 10.2: Synchronizace názvů
**Ověření:**
- Názvy produktů odpovídají katalogu

**Očekávaný výsledek:**
- Konzistentní názvy napříč systémem