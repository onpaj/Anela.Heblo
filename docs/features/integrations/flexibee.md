# FlexiBee Integrace

> **Status**: Plánováno  
> **Poslední aktualizace**: $(Get-Date -Format "dd.MM.yyyy")

## Přehled

FlexiBee je účetní systém, se kterým Anela.Heblo.Blazor synchronizuje data pro:
- Produkty a katalogy
- Faktury a doklady
- Zásoby a sklady
- Zákazníky a dodavatele

## Architektura integrace

### Adapter Pattern
- `Anela.Heblo.Adapters.Flexi` - Hlavní adapter
- `FlexiBeeSDK` - Klient pro komunikaci s FlexiBee API
- `HebloFlexiAdapterModule` - ABP modul pro registraci služeb

### Synchronizace
- **Bidirectional sync** - Data se synchronizují oběma směry
- **Event-driven** - Změny se propagují přes events
- **Batch processing** - Hromadné zpracování pro výkon

## Konfigurace

### appsettings.json
```json
{
  "FlexiBee": {
    "BaseUrl": "https://demo.flexibee.eu",
    "Company": "demo",
    "Username": "admin",
    "Password": "admin123",
    "ApiVersion": "1.0"
  }
}
```

### Environment Variables
- `FLEXIBEE_BASE_URL` - URL FlexiBee instance
- `FLEXIBEE_COMPANY` - Název společnosti
- `FLEXIBEE_USERNAME` - API uživatel
- `FLEXIBEE_PASSWORD` - API heslo

## API Endpoints

### Produkty
- `GET /c/company/adresar` - Seznam produktů
- `POST /c/company/adresar` - Vytvoření produktu
- `PUT /c/company/adresar/{id}` - Aktualizace produktu

### Faktury
- `GET /c/company/faktura-vydana` - Vydané faktury
- `GET /c/company/faktura-prijata` - Přijaté faktury

### Sklady
- `GET /c/company/sklad` - Seznam skladů
- `GET /c/company/sklad-pohyb` - Pohyby na skladě

## Data Mapping

### Produkty
| Anela.Heblo | FlexiBee |
|-------------|----------|
| Product.Code | kod |
| Product.Name | nazev |
| Product.Price | cenaZaklad |

### Faktury
| Anela.Heblo | FlexiBee |
|-------------|----------|
| Invoice.Number | kod |
| Invoice.Date | datVyst |
| Invoice.Amount | celkem |

## Error Handling

### Retry Policy
- Exponential backoff
- Maximum 3 pokusy
- Circuit breaker pattern

### Logging
- Strukturované logy pro debugging
- Error tracking pro monitoring
- Audit trail pro změny

## Monitoring

### Health Checks
- `/health/flexibee` - Stav připojení
- Response time monitoring
- Error rate tracking

### Metrics
- Počet synchronizovaných záznamů
- Doba synchronizace
- Chybovost API calls

---

*Tento dokument bude doplněn o konkrétní implementační detaily a příklady.* 