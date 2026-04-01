# Shoptet API - Podklady pro integraci

**Připraveno:** 2026-03-24
**API Dokumentace:** https://api.docs.shoptet.com/shoptet-api/openapi
**Developer portal:** https://developers.shoptet.com/api/documentation/

---

## 1. Autentizace

Shoptet API používá dvoustupňový token systém.

### 1.1 OAuth Access Token
- Získáš při instalaci addonu do e-shopu
- Délka: 255 znaků
- Platnost: neomezená
- Slouží k získání API Access Tokenu

### 1.2 API Access Token
- Získáš voláním OAuth serveru s OAuth Access Tokenem
- Délka: 38–60 znaků
- Platnost: **omezená** (vrací se `expires_in` v odpovědi)
- Maximálně 5 platných tokenů současně

### 1.3 Flow získání tokenu

```
POST https://<eshop-adresa>/action/ApiOAuthServer/getAccessToken
Authorization: Bearer <oauth_access_token>
```

**Response:**
```json
{
  "access_token": "...",
  "expires_in": 3600
}
```

### 1.4 Použití v requestech

Každý API request musí obsahovat header:
```
Shoptet-Access-Token: <api_access_token>
```

### 1.5 Doporučení pro implementaci
- Cachovat API Access Token a obnovovat před expirací
- Ošetřit 401 odpověď automatickým obnovením tokenu
- Token si uživatel zajistí sám (mimo scope této integrace)

---

## 2. Obecné principy API

### 2.1 Base URL
```
https://<eshop-adresa>/api/
```

### 2.2 Rate Limiting
- Max **50 současných spojení** z jedné IP adresy
- Max **3 současná spojení** pro jeden token
- Při překročení: HTTP **429** Too Many Requests
- Sledovat headery: `X-RateLimit-Bucket-Filling` (v každé odpovědi), `Retry-After` (jen při naplnění bucketu)
- **Doporučení:** Batch operace kde je to možné (max 100 položek v jednom requestu pro stock update)

### 2.3 Stránkování (Pagination)
- První stránka = **1** (ne 0)
- Parametry: `page`, `itemsPerPage`
- Default a max `itemsPerPage` se liší dle endpointu
- Pokud zadáš vyšší než max, automaticky se použije max
- **Pozor:** Při stránkování kontrolovat `totalCount` — pokud se data mění za běhu, položky se mohou posunout

### 2.4 Status kódy
- **200** OK
- **400** Bad Request
- **401** Unauthorized (token expiroval)
- **404** Not Found
- **423** Locked (parallel request na PDF/ISDOC download)
- **429** Too Many Requests (rate limit)

### 2.5 Kódy (identifikátory)
- `code` u faktur, objednávek atd. může obsahovat **písmena, pomlčky a další znaky** — ne jen čísla!

---

## 3. Fáze 1: Stahování vyúčtování ShoptetPay

### 3.1 Kontext

ShoptetPay má **vlastní, separátní API** — nezávislé na hlavním Shoptet API. Má jiný base URL, jiný auth token a vlastní endpointy.

**Dokumentace:** https://api.docs.shoptet.com/shoptet-pay-api/openapi-public

### 3.2 Autentizace (odlišná od hlavního Shoptet API!)

- API klíč se generuje v **administraci e-shopu → Shoptet Pay → Online karetní platby** (nebo Payment Terminals) → záložka **API keys Settings**
- Klik na **Generate new key** → zadat název, expiraci, permissions
- **Pozor:** Klíč se zobrazí jen jednou, po refreshi stránky už ne!
- Použití: HTTP header `Authorization: Bearer <API access token>`

### 3.3 Servery

| Prostředí | URL |
|-----------|-----|
| **Production** | `https://api.shoptetpay.com` |
| Test | `https://api.labshoptetpay.com` |

### 3.4 Payout Reports — Vyúčtování (KLÍČOVÁ SEKCE)

#### GET /v1/reports/payout — Seznam vyúčtování

```
GET https://api.shoptetpay.com/v1/reports/payout
Authorization: Bearer <shoptet-pay-api-token>
```

**Query parametry:**

| Parametr | Povinný | Typ | Popis |
|----------|---------|-----|-------|
| `id` | ne | string | ID konkrétního reportu |
| `dateFrom` | ne | datetime (ISO 8601) | Datum od |
| `dateTo` | ne | datetime (ISO 8601) | Datum do |
| `types` | ne | enum | Typ reportu: `PAYOUT`, `WEEKLY`, `MONTHLY` |
| `states` | ne | enum | Stav: `VALID`, `INVALID` |
| `limit` | ne | number | Max 1000, default 50 |
| `offset` | ne | number | Offset pro stránkování, default 0 |

**Response — PublicReportOutputDto:**
```
id              - string, ID reportu
currency        - string, enum (CZK, EUR, USD, GBP, PLN, HUF, RON, ...)
type            - enum: PAYOUT | WEEKLY | MONTHLY
serialNumber    - number, pořadové číslo
dateFrom        - datetime, období od
dateTo          - datetime, období do
createdAt       - datetime, datum vytvoření
```

#### GET /v1/reports/payout/{id}/{format} — Stažení reportu ve formátu

```
GET https://api.shoptetpay.com/v1/reports/payout/{id}/{format}
```

**Path parametry:**

| Parametr | Typ | Popis |
|----------|-----|-------|
| `id` | string (12-21 znaků) | ID reportu |
| `format` | enum | Formát: **`abo`**, `csv`, `xml`, `xlsx` |

**Formáty:**
- **`abo`** — bankovní ABO formát (ten samý co se stahuje z administrace)
- **`csv`** — CSV export
- **`xml`** — XML export
- **`xlsx`** — Excel export

### 3.5 Card Payments — Karetní platby

#### GET /v1/card-payments — Seznam plateb pro objednávku

```
GET https://api.shoptetpay.com/v1/card-payments?orderCode=2026000123
```

**Povinný parametr:** `orderCode` (string)

#### GET /v1/card-payments/{id} — Detail platby

```
GET https://api.shoptetpay.com/v1/card-payments/{id}
```

**Response — CardPaymentsOutputDto:**
```
id                  - string
createdAt           - datetime
paymentMethod:
  name              - string | null (např. "mc", "visa")
  variant           - string | null
  cardSummary       - string | null (poslední 4 číslice)
  cardBin           - string | null
  maskedCardNumber  - string | null
  cardExpiry        - string | null
  merchantAdviceCode - string | null
reasonCode          - string | null (kód důvodu odmítnutí)
recurringPaymentId  - string | null
amount:
  value             - string (částka)
  currencyCode      - enum (CZK, EUR, ...)
state               - enum: CREATED | REDIRECT_ACCEPTED | PENDING | CANCELLED | ERROR | FINISHED
orderCode           - string (kód objednávky)
paymentCode         - string
customerGuid        - string | null
subscription        - boolean
initial             - boolean
processingModel     - enum: unsaved | new_save | saved (nullable)
refunds[]           - pole RefundItemOutputDto
preAuthData         - PreAuthDataOutputDto (pre-autorizace)
```

#### Další card-payment endpointy

| Metoda | Endpoint | Popis |
|--------|----------|-------|
| POST | `/v1/card-payments` | Vytvoření platby |
| POST | `/v1/card-payments/{id}/revoke` | Zrušení platby |
| POST | `/v1/card-payments/{id}/capture` | Capture pre-autorizace |
| POST | `/v1/card-payments/{id}/cancel` | Storno |
| POST | `/v1/card-payments/{id}/refund` | Refundace |
| GET | `/v1/card-payments/{id}/events` | Události platby |

### 3.6 ShoptetPay Webhooky

**Registrace:** `POST /v1/webhooks`
**Seznam:** `GET /v1/webhooks`
**Detail:** `GET /v1/webhooks/{id}`
**Smazání:** `DELETE /v1/webhooks/{id}`

**Dostupné eventy:**
- `cardPaymentState:change` — změna stavu karetní platby
- `cardPayment:refund` — refundace
- `paymentState:change` — změna stavu platby
- `payment:refund` — refundace platby
- `preAuth:change` — změna pre-autorizace

**Webhook body:**
```json
{
  "url": "https://your-app.com/webhook",
  "description": "ShoptetPay notifications",
  "event": "cardPaymentState:change"
}
```

### 3.7 Doporučený flow pro stahování vyúčtování

```
1. GET /v1/reports/payout?dateFrom=2026-01-01&dateTo=2026-03-24&types=PAYOUT
   → seznam payout reportů (id, currency, období, ...)
2. Pro každý report: GET /v1/reports/payout/{id}/abo
   → stažení ABO souboru (bankovní formát pro import)
   NEBO: GET /v1/reports/payout/{id}/csv → CSV
   NEBO: GET /v1/reports/payout/{id}/xml → XML
3. Pro detail karetních plateb: GET /v1/card-payments?orderCode={code}
   → detaily jednotlivých transakcí
```

### 3.8 Důležité poznámky
- ShoptetPay API je **separátní od hlavního Shoptet API** — jiný base URL (`api.shoptetpay.com`), jiný token
- Token se generuje přímo v Shoptet Pay administraci, ne přes OAuth flow hlavního API
- Formát `abo` je přesně ten samý co se stahuje z administrace manuálně
- Pro kompletní přehled plateb kombinovat payout reports s card-payments endpointem

---

## 4. Fáze 2: Skladové operace (naskladnění, inventarizace)

### 4.1 Přehled endpointů

| Metoda | Endpoint | Popis |
|--------|----------|-------|
| GET | `/api/stocks` | Seznam všech skladů |
| GET | `/api/stocks/{stockId}` | Detail skladu |
| GET | `/api/stocks/{stockId}/movements` | Pohyby na skladě (log změn) |
| GET | `/api/stocks/{stockId}/movements/last` | Poslední pohyb na skladě |
| PATCH | `/api/stocks/{stockId}/movements` | **Změna množství na skladě** |
| GET | `/api/stocks/{stockId}/supplies` | Zásoby produktů na skladě |

### 4.2 Typy skladů
- `internalPhysical` — výchozí fyzický sklad
- `internalVirtual` — logické rozdělení s parent skladem
- `external` — externí standalone sklad

### 4.3 GET /api/stocks — Seznam skladů

```
GET /api/stocks
```

**Response:** Vrací všechny sklady bez stránkování (typicky jich není mnoho).

### 4.4 PATCH /api/stocks/{stockId}/movements — Změna množství (KLÍČOVÝ ENDPOINT)

```
PATCH /api/stocks/{stockId}/movements
Content-Type: application/json
Shoptet-Access-Token: <token>
```

**Request body:**
```json
{
  "data": [
    {
      "productCode": "43/BIL",
      "amountChange": 10
    },
    {
      "productCode": "SKU-002",
      "amountChange": -3
    }
  ]
}
```

**Klíčové informace:**
- `productCode` — **povinný**, kód produktu/varianty
- `amountChange` — relativní změna množství (kladné = naskladnění, záporné = vyskladnění)
- Max **100 produktů/variant** v jednom requestu
- Podporuje i absolutní nastavení množství (ověřit v dokumentaci přesný parametr)

**Use cases:**
- **Naskladnění:** `amountChange: +10` (přibylo 10 ks)
- **Vyskladnění:** `amountChange: -5` (ubylo 5 ks)
- **Inventarizace:** Nastavit absolutní hodnotu (ověřit podporu v API)

### 4.5 GET /api/stocks/{stockId}/movements — Pohyby na skladě

```
GET /api/stocks/{stockId}/movements
```

**Popis:** Vrací chronologický log všech změn množství na daném skladě. Podporuje stránkování a `include` parametr pro volitelné sekce.

**Použití:** Sledování historie skladových pohybů, audit, synchronizace.

### 4.6 GET /api/stocks/{stockId}/movements/last — Poslední pohyb

```
GET /api/stocks/{stockId}/movements/last
```

**Popis:** Vrací ID a timestamp posledního pohybu. Užitečné pro rychlé zjištění, zda se něco změnilo, a pro nastavení startovního bodu při synchronizaci.

### 4.7 GET /api/stocks/{stockId}/supplies — Zásoby

```
GET /api/stocks/{stockId}/supplies
```

**Popis:** Vrací aktuální zásoby pro všechny produkty na daném skladě — GUID, code a quantity pro každou variantu.

### 4.8 Doporučený flow pro naskladnění

```
1. GET /api/stocks → získat stockId
2. PATCH /api/stocks/{stockId}/movements → nastavit množství
3. GET /api/stocks/{stockId}/supplies → ověřit výsledek
```

### 4.9 Doporučený flow pro inventarizaci

```
1. GET /api/stocks/{stockId}/supplies → aktuální stav
2. Porovnat s fyzickým stavem
3. PATCH /api/stocks/{stockId}/movements → opravit rozdíly
4. GET /api/stocks/{stockId}/supplies → ověřit
```

### 4.10 Důležité upozornění
- Skladové změny **neimplikují změnu produktu** (stock changes do not imply product change) — webhook pro product change se nefiruje při stock update

---

## 5. Fáze 3: Stahování vydaných faktur (strukturovaná data pro import do účetního systému)

### 5.1 Přehled endpointů

| Metoda | Endpoint | Popis | Formát |
|--------|----------|-------|--------|
| GET | `/api/invoices` | Seznam faktur (s filtrováním) | **JSON** |
| GET | `/api/invoices/{code}` | Detail faktury | **JSON** |
| GET | `/api/invoices/{code}/isdoc` | Stažení faktury jako ISDOC | **XML (ISDOC)** |
| GET | `/api/invoices/{code}/pdf` | Stažení faktury jako PDF | PDF (binární) |
| POST | `/api/invoices` | Vytvoření faktury z objednávky | JSON |

**Pro import do účetního systému jsou relevantní:**
1. **JSON response z `/api/invoices`** — strukturovaná data, ideální pro programatický import
2. **ISDOC z `/api/invoices/{code}/isdoc`** — český standard elektronické faktury (XML), přímý import do většiny českých účetních systémů (Pohoda, Money, FlexiBee, ABRA...)

### 5.2 GET /api/invoices — Seznam faktur

```
GET /api/invoices?creationTimeFrom=2026-01-01&creationTimeTo=2026-03-24&page=1&itemsPerPage=20
```

**Filtrovací parametry:**

| Parametr | Typ | Popis |
|----------|-----|-------|
| `creationTimeFrom` / `creationTimeTo` | datetime | Datum vytvoření |
| `changeTimeFrom` / `changeTimeTo` | datetime | Datum změny |
| `codeFrom` / `codeTo` | string | Rozsah čísel faktur |
| `dueDateFrom` / `dueDateTo` | date | Datum splatnosti |
| `taxDateFrom` / `taxDateTo` | date | Datum zdanitelného plnění |
| `orderCodeFrom` / `orderCodeTo` | string | Filtr dle čísla objednávky |
| `orderCode` | string | Přesný kód objednávky |
| `customerGuid` | string | GUID zákazníka |
| `varSymbol` | string | Variabilní symbol |
| `isValid` | boolean | Platnost faktury |
| `hasTaxId` | boolean | Má DIČ |
| `hasVatId` | boolean | Má VAT ID |
| `hasCompanyId` | boolean | Má IČO |
| `include` | string | Volitelné sekce (např. `surchargeParameters`) |

**Response:** GZIP komprimovaná. Každá faktura v seznamu má stejný formát jako detail faktury.

### 5.3 GET /api/invoices/{code} — Detail faktury

```
GET /api/invoices/{code}
```

**Klíčová pole v odpovědi:**

```
code                    - Číslo faktury
varSymbol               - Variabilní symbol
orderCode               - Číslo objednávky
proformaInvoiceCodes[]  - Kódy zálohových faktur (pole, nahrazuje deprecated proformaInvoiceCode)
creationTime            - Datum vytvoření
changeTime              - Datum změny
dueDate                 - Datum splatnosti
taxDate                 - DUZP
isValid                 - Platnost

billingAddress:
  company               - Firma
  fullName              - Jméno
  street                - Ulice
  city                  - Město
  zip                   - PSČ
  countryCode           - Kód země
  companyId              - IČO
  vatId                 - DIČ

items[]:
  productCode           - Kód produktu
  name                  - Název
  amount / quantity     - Množství
  unitPrice:
    withVat             - Cena s DPH
    withoutVat          - Cena bez DPH
    vat                 - DPH částka
    vatRate             - Sazba DPH

price:
  withVat               - Celkem s DPH
  withoutVat            - Celkem bez DPH
  toPay                 - K úhradě
  currencyCode          - Měna

billingMethod           - Způsob fakturace
```

**Poznámka:** Pro přesný tvar response viz kódový seznam billing methods: https://api.docs.shoptet.com/shoptet-api/openapi/section/code-lists/invoice-billing-methods

### 5.4 GET /api/invoices/{code}/isdoc — Stažení ISDOC (XML)

```
GET /api/invoices/{code}/isdoc
```

**Response:** `Content-Type: application/octet-stream` — ISDOC XML soubor.

**ISDOC** je český standard elektronické faktury (XML). Většina českých účetních systémů (Pohoda, Money S3/S5, FlexiBee/ABRA, Vario...) umí ISDOC přímo importovat.

**Omezení:**
- Stahovat lze **pouze po jednom** pro každý e-shop
- Paralelní requesty skončí s **423 Locked**
- Faktura musí být **předem vygenerovaná**

### 5.5 GET /api/invoices/{code}/pdf — Stažení PDF (volitelné)

```
GET /api/invoices/{code}/pdf
```

Stejná omezení jako ISDOC. Pro import do účetního systému **nepotřebuješ** — použij JSON nebo ISDOC.

### 5.6 Doporučený flow pro import faktur do účetního systému

**Varianta A: JSON → vlastní transformace**
```
1. GET /api/invoices?creationTimeFrom=...&creationTimeTo=... → seznam faktur (JSON, kompletní data)
2. Každá faktura v seznamu má PLNÝ detail (stejný formát jako /api/invoices/{code})
3. Transformovat JSON na formát účetního systému
4. Ukládat changeTime pro inkrementální sync
```

**Varianta B: ISDOC → přímý import**
```
1. GET /api/invoices?creationTimeFrom=...&creationTimeTo=... → seznam faktur (získat kódy)
2. Pro každou fakturu: GET /api/invoices/{code}/isdoc → stáhnout ISDOC XML
   ⚠️ Sekvenčně, ne paralelně! (423 Locked)
3. Importovat ISDOC do účetního systému
4. Ukládat changeTime pro inkrementální sync
```

**Doporučení:** Varianta A je rychlejší (jeden request = všechny faktury), flexibilnější a bez omezení paralelismu. Varianta B je jednodušší pokud účetní systém umí ISDOC nativně.

---

## 6. Webhooky

Shoptet podporuje webhooky — HTTPS cally, které posílají JSON payload na registrovanou URL při specifických eventech.

**Dokumentace:** https://developers.shoptet.com/api/documentation/webhooks/

### 6.1 Registrace webhooků
- Endpoint: viz `/api/webhooks` v dokumentaci
- Kompletní seznam event typů v PHP SDK: `\Shoptet\Api\Sdk\Php\Webhook\Event` enum
- Detail registrovaných webhooků: `GET /api/webhooks`

### 6.2 Relevantní eventy pro tuto integraci
- `order:create` — vytvoření objednávky (ověřeno)
- `invoice:create` — vytvoření faktury (ověřit v dokumentaci)
- `job:finished` — **povinný** pro asynchronní requesty
- Skladové eventy — ověřit dostupnost v aktuální verzi API

### 6.3 Poznámka
Kompletní seznam webhook eventů je třeba ověřit v aktuální dokumentaci nebo v SDK.

---

## 7. Asynchronní requesty

Některé endpointy (snapshot/batch operace) jsou asynchronní — response neobsahuje data, ale `jobId`.

### 7.1 Flow
```
1. POST/GET asynchronní endpoint → response s jobId
2. Čekat na webhook job:finished
3. GET /api/system/jobs/{jobId} → zjistit resultUrl
4. GET {resultUrl} → stáhnout výsledek
```

### 7.2 Důležité
- **Registrace webhoku `job:finished` je POVINNÁ** — bez ní asynchronní requesty vrací 403
- `GET /api/system/jobs/` — seznam všech jobů (fronta, běžící, hotové — historie 30 dní)
- Po dokončení jobu: v `resultUrl` je URL ke stažení výsledku

---

## 8. Dostupné SDK a nástroje

- **GitHub repo:** https://github.com/shoptet/developers — obsahuje `openapi/openapi.yaml`
- **API SDK:** https://github.com/shoptet/api-sdk
- **Postman kolekce:** Import z `openapi.yaml` nebo fork z Shoptet Public API Workspace
- **OpenAPI spec:** `openapi/openapi.yaml` v GitHub repu — lze použít pro generování klientů

---

## 9. Implementační checklist

### Fáze 1: ShoptetPay vyúčtování (SEPARÁTNÍ API — api.shoptetpay.com)
- [ ] Vygenerovat API klíč v administraci Shoptet Pay (manuálně)
- [ ] `GET /v1/reports/payout` — seznam payout reportů s filtry (dateFrom, dateTo, types)
- [ ] `GET /v1/reports/payout/{id}/abo` — stažení ABO exportu (nebo csv/xml/xlsx)
- [ ] `GET /v1/card-payments?orderCode={code}` — detail karetních plateb
- [ ] Registrovat webhook `cardPaymentState:change` pro real-time notifikace
- [ ] Implementovat stahování a parsování reportů

### Fáze 2: Skladové operace
- [ ] `GET /api/stocks` — získat seznam skladů a ID
- [ ] `GET /api/stocks/{stockId}/supplies` — aktuální zásoby
- [ ] `PATCH /api/stocks/{stockId}/movements` — naskladnění/vyskladnění (batch max 100)
- [ ] `GET /api/stocks/{stockId}/movements` — ověření a audit
- [ ] Implementovat inventarizační porovnání a hromadnou opravu

### Fáze 3: Vydané faktury (import do účetního systému)
- [ ] `GET /api/invoices` s filtry — JSON data faktur (hlavní zdroj)
- [ ] Implementovat transformaci JSON → formát účetního systému
- [ ] Alternativně: `GET /api/invoices/{code}/isdoc` — ISDOC XML pro přímý import (sekvenčně!)
- [ ] Inkrementální sync přes `changeTimeFrom`
- [ ] Rozhodnout: JSON transformace vs. ISDOC import (dle účetního systému)

### Průřezové
- [ ] Implementovat token management (cache + auto-refresh)
- [ ] Rate limiter (max 3 spojení/token, sledovat `X-RateLimit-Bucket-Filling`)
- [ ] Pagination helper (page=1, kontrola totalCount)
- [ ] Error handling (401 → refresh token, 423 → retry, 429 → backoff)
- [ ] Stáhnout `openapi.yaml` z GitHubu a vygenerovat typy/klienta

---

## 10. Otevřené otázky

1. **Absolutní stock:** Podporuje `PATCH /stocks/{stockId}/movements` absolutní nastavení množství (pro inventarizaci), nebo jen relativní `amountChange`?
2. **Webhooky pro sklad:** Jaké webhook eventy jsou dostupné v hlavním Shoptet API pro skladové změny?
3. **Invoice JSON completeness:** Obsahuje JSON response z `GET /api/invoices` všechna pole potřebná pro účetní import (DUZP, sazby DPH, základ daně dle sazeb, forma úhrady)?
4. **ShoptetPay permissions:** Jaké permissions jsou potřeba při generování API klíče pro přístup k payout reports?
