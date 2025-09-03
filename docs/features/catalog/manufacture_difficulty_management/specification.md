# Manufacture Difficulty Management - Časová Evidence

## Přehled
Manufacture Difficulty je vlastnost produktu, která se mění v čase. Každý produkt může mít více hodnot difficulty s různými obdobími platnosti.

## Funkční požadavky

### 1. Časová Evidence
- **Platnost od-do**: Každá hodnota difficulty má definované období platnosti
- **Bez překryvu**: Platnosti se nesmí překrývat pro stejný produkt
- **Bez mezer**: Kontinuální pokrytí
- **Aktuální hodnota**: Systém vrací difficulty platnou k aktuálnímu dni

### 2. Správa v Catalog Detail
- **Editace produktu**: Možnost přidat/upravit manufacture difficulty
- **Seznam platností**: Zobrazení všech historických i budoucích hodnot
- **Validace překryvů**: Kontrola kolizí při zadávání nových období
- **Aktivní hodnota**: Zvýraznění aktuálně platné difficulty

### 3. Načítání v Catalog Aggregate
- **Automatické filtrování**: Aggregate načítá pouze aktuálně platnou hodnotu
- **Datum reference**: Defaultně `TimeProvider.UtcNow`, možnost specifikovat jiné datum
- **Fallback strategie**: Co dělat když není platná hodnota k danému datu

## Datový model

### ManufactureDifficultyHistory
```
- ProductCode (FK)
- DifficultyValue (int)
- ValidFrom (DateTime?, UTC) 
- ValidTo (DateTime?, UTC)
- CreatedAt (DateTime)
- CreatedBy (UserId)
```

### Catalog Aggregate Integration

Nastaveni se nacte do CatalogAggregate.ManufactureDifficultySettings, rovnou se nastavi i aktualni property ManufactureDifficulty jako hodnota platka k aktualnimu dat. Vse se nacita v CatalogRepository.Merge
```
- ManufactureDifficultyRepository.ListAsync(string? productCode, DateTime? asOfDate = null)
- ManufactureDifficultyRepository.GetAsync(string productCode, DateTime? asOfDate = null)
```

## Business pravidla

### 1. Platnost
- `ValidFrom` je volitelne (null = plati "odjakziva"). POuze jediny zaznam pro produkt muze mi ValidFrom = null
- `ValidTo` je volitelné (null = platí do odvolání). POuze jediny zaznam pro produkt muze mit ValidTo = null
- `ValidFrom` < `ValidTo` (pokud je ValidTo a ValidFrom zadáno)
- Nové záznamy nemohou mít překrývající se platnosti

### 2. Historie
- Staré záznamy se nemazají, pouze se uzavírají (`ValidTo`)
- Úprava současné platnosti může vytvořit nový záznam

### 3. Výchozí hodnoty
- Nový produkt nema vychozi difficulty, neni nutne, aby ji mel kazdy produkt

## UI/UX požadavky

### Catalog Detail Page
- Zobrazuje se aktualni hodnota (K dnesnimu dni), jde rozkliknout na modal window s nastavenim ManufactureDifficulty
- **Add/Edit form**: Formulář pro nové období platnosti
- **Current indicator**: Jasné označení aktuálně platné hodnoty
- **Validation messages**: Chyby při překryvech nebo neplatných datech

### Seznam produktů
- Zobrazuje pouze aktuální difficulty (vzdy se nacita jedina hodnota z CatalogAggregate)

## Integration Points

### Stávající systémy
- **Catalog Module**: Pracuje se vzdy s aktualni hodnotou
- **Manufacture Module**: Pracuje se vzdy s aktualni hodnotou
- **Margin Analysis module**: pracuje se s hodnotou platnou vzdy pro prvni den daneho mesice. Data se mohou nacitat CatalogAggregate.ManufactureDifficultySettings
- **API endpoints**: Nové endpointy pro správu historie

### Budoucí rozšíření
- Bulk import nastaveni

## Migrace

### Existující data
- Všechny stávající difficulty hodnoty dostanou `ValidFrom = null`
- `ValidTo = null` (platí do odvolání)
- Žádná ztráta dat

### Zpětná kompatibilita
- Stávající API endpointy fungují stejně
- Nové endpointy pro správu historie
- Postupná migrace UI komponent