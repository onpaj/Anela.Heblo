# Specifikace systému zpracování bankovních výpisů Comgate

## Účel a obchodní kontext

Systém zpracování bankovních výpisů Comgate zajišťuje automatizovaný import a zpracování bankovních transakcí z platební brány Comgate do účetního systému FlexiBee. Slouží jako propojení mezi platebními systémy společnosti a účetnictvím pro zajištění aktuálního stavu bankovních účtů a transakcí.

### Hlavní funkce systému:
- Automatický denní import bankovních výpisů z Comgate
- Manuální import výpisů na vyžádání 
- Transformace a přenos dat do účetního systému FlexiBee
- Auditní trail importovaných výpisů
- Správa více bankovních účtů (CZK i EUR)

## Obchodní procesy

### 1. Automatický denní import

**Spouštěč**: Naplánované úlohy (Cron jobs)
- **CZK účet**: Každý den v 9:00 (Cron: "0 9 * * *")  
- **EUR účet**: Každý den v 9:10 (Cron: "10 9 * * *")

**Proces**:
1. Systém automaticky načte výpisy za předchozí den
2. Pro každý konfigurovaný účet (ComgateCZK, ComgateEUR):
   - Zavolá Comgate API pro získání seznamu výpisů
   - Stáhne detailní data každého výpisu ve formátu ABO
   - Předá data do FlexiBee pro zpracování
   - Zaznamená výsledek importu do databáze

**Vstupní parametry**:
- Název účtu (ComgateCZK/ComgateEUR) 
- Datum pro import (včera)

**Výstup**:
- Záznam o provedeném importu s počtem položek a stavem

### 2. Manuální import

**Spouštěč**: Uživatel prostřednictvím webového rozhraní

**Proces**:
1. Uživatel na stránce `/finance/comgate`:
   - Vybere datum pro import
   - Vybere měnu/účet (CZK/EUR)
   - Spustí import tlačítkem
2. Systém provede stejný proces jako při automatickém importu
3. Zobrazí výsledek uživateli v tabulce importovaných výpisů

**Validace**:
- Datum musí být platné
- Název účtu musí existovat v konfiguraci

### 3. Zobrazení historie importů

**Spouštěč**: Zobrazení stránky finance/comgate

**Funkcionalita**:
- Tabulka všech provedených importů s filtračními možnostmi
- Řazení podle data importu (sestupně)
- Filtrování podle ID, data výpisu a data importu
- Zobrazení počtu položek a typu chyb

## Integrace a datové toky

### 1. Integrace s Comgate API

**Endpoint pro seznam výpisů**:
```
POST https://payments.comgate.cz/v1.0/transferList?merchant={merchantId}&secret={secret}&date={datum}
```

**Endpoint pro detail výpisu**:
```
GET https://payments.comgate.cz/v1.0/aboSingleTransfer?merchant={merchantId}&secret={secret}&transferId={transferId}&download=true&type=v2
```

**Autentizace**: 
- MerchantId a Secret z konfigurace
- Předávány jako URL parametry

**Formát dat**:
- Seznam výpisů: JSON s poli TransferId, TransferDate, AccountCounterParty
- Detail výpisu: ABO formát (textový formát bankovních výpisů)

### 2. Integrace s FlexiBee

**Rozhraní**: IBankAccountClient.ImportStatement()

**Proces**:
- Přenos ABO dat do FlexiBee účtu dle konfigurace
- FlexiBeeId se mapuje podle názvu účtu z konfigurace
- Vrací výsledek s indikátorem úspěchu/chyby

### 3. Databázové entity

**BankStatementImport** (hlavní entita):
- **Id**: Unikátní identifikátor záznamu
- **TransferId**: ID výpisu z Comgate  
- **StatementDate**: Datum výpisu
- **ImportDate**: Časová značka importu
- **Account**: Číslo bankovního účtu
- **Currency**: Měna (CZK/EUR)
- **ItemCount**: Počet položek ve výpisu
- **ImportResult**: Výsledek importu ("OK" nebo chybová zpráva)

## Obchodní pravidla a logika

### 1. Konfigurace účtů

Systém podporuje více bankovních účtů definovaných v konfiguraci:

```json
{
  "BankAccounts": {
    "Accounts": [
      {
        "Name": "ComgateCZK",
        "AccountNumber": "číslo_účtu_CZK", 
        "FlexiBeeId": 123
      },
      {
        "Name": "ComgateEUR",
        "AccountNumber": "číslo_účtu_EUR",
        "FlexiBeeId": 456
      }
    ]
  }
}
```

### 2. Určení měny

Měna se odvozuje od názvu účtu:
- Pokud název končí na "EUR" → EUR
- Jinak → CZK (výchozí)

### 3. Filtrace výpisů

Při získávání výpisů z Comgate se filtrují pouze výpisy pro správný bankovní účet podle AccountCounterParty.

### 4. Zpracování chyb

- Při chybě komunikace s Comgate se proces zastaví
- Při chybě v FlexiBee se zaznamená chybový stav do ImportResult
- Chyby při neexistujícím účtu vyhodí validační výjimku

## Specifikace vstupů a výstupů

### API Endpoint: POST /api/bank-statements/import

**Vstup (BankImportRequestDto)**:
```json
{
  "AccountName": "ComgateCZK",     // Povinné: název účtu z konfigurace
  "StatementDate": "2024-12-05"   // Povinné: datum výpisu ve formátu ISO
}
```

**Výstup (BankStatementImportResultDto)**:
```json
{
  "Statements": [
    {
      "Id": 123,
      "ItemCount": 15,
      "ErrorType": null,
      "StatementDate": "2024-12-05T00:00:00",
      "ImportDate": "2024-12-06T09:30:00"
    }
  ]
}
```

### Query API: GET /api/bank-statements

**Query parametry (BankStatementImportQueryDto)**:
- **Id**: Filtrace podle ID záznamu
- **StatementDate**: Filtrace podle data výpisu  
- **ImportDate**: Filtrace podle data importu
- Standardní ABP parametry pro stránkování a řazení

**Výstup**: Stránkovaný seznam BankStatementImportDto

## Řízení chyb

### 1. Chyby konfigurace
- **Případ**: Neexistující název účtu
- **Reakce**: AbpValidationException s popisem chyby
- **Řešení**: Kontrola konfigurace BankAccounts

### 2. Chyby komunikace s Comgate
- **Případ**: Síťové chyby, neplatné autentizační údaje
- **Reakce**: HTTP výjimka s detailem problému
- **Řešení**: Kontrola síťového připojení a konfigurace Comgate

### 3. Chyby zpracování ve FlexiBee
- **Případ**: Chyby při importu do účetního systému
- **Reakce**: Záznam chyby do ImportResult, pokračování procesu
- **Řešení**: Manuální kontrola a oprava v FlexiBee

### 4. Chyby parsování ABO formátu
- **Případ**: Neplatný nebo poškozený ABO soubor
- **Reakce**: Výjimka při parsování
- **Řešení**: Kontrola formátu dat z Comgate

## Bezpečnostní aspekty

### 1. Autentizace API
- Všechny endpoint vyžadují autorizaci (@Authorize)
- Kromě interního ImportStatements, který je označen jako RemoteService(IsEnabled = false)

### 2. Konfigurace přístupových údajů
- MerchantId a Secret jsou uloženy v konfiguraci aplikace
- Měly by být zabezpečeny prostřednictvím Azure Key Vault nebo podobného systému

### 3. Auditní trail
- Všechny importy jsou zaznamenávány s časovou značkou
- Sledování změn prostřednictvím AuditedAggregateRoot

## Monitorování a logging

### 1. Úlohy na pozadí
- Denní importy jsou prováděny jako Hangfire úlohy
- Možnost sledování stavu a historie úloh v Hangfire dashboardu

### 2. Aplikační logování
- ILogger<ComgateStatementsAppService> pro zaznamenávání operací
- Doporučuje se logování úspěšných i neúspěšných importů

### 3. Kontrola povolení úloh
- Každá automatická úloha kontroluje, zda je povolená před spuštěním
- Řízení prostřednictvím IJobsAppService.IsEnabled()

## Výkonnostní charakteristiky

### 1. Frekvence importů
- Automatické importy 1x denně pro každý účet
- Manuální importy dle potřeby uživatele

### 2. Velikost dat
- Typicky desítky až stovky transakcí denně
- ABO formát je textový, relativně kompaktní

### 3. Doba zpracování
- Závislá na počtu výpisů a rychlosti FlexiBee API
- Typicky jednotky sekund až minut

## Rozšiřitelnost

### 1. Přidání dalších bank
- Implementace nového IBankClient pro jinou banku
- Rozšíření konfigurace BankAccountSettings
- Implementace parseru pro specifický formát bank

### 2. Dodatečná validace
- Rozšíření business logic pro kontroly transakcí
- Implementace pravidel pro automatické párování plateb

### 3. Notifikace
- Přidání emailových/SMS notifikací při chybách
- Dashboard pro monitoring stavu importů