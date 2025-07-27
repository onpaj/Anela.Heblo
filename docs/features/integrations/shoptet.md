# Shoptet Integrace

> **Status**: Plánováno  
> **Poslední aktualizace**: $(Get-Date -Format "dd.MM.yyyy")

## Přehled

Shoptet je e-shop platforma, se kterou Anela.Heblo.Blazor synchronizuje:
- Produkty a katalogy
- Objednávky a zákazníky
- Sklady a dostupnost
- Ceny a slevy

## Architektura integrace

### Adapter Pattern
- `Anela.Heblo.Adapters.Shoptet` - Hlavní adapter
- `ShoptetApiClient` - Klient pro Shoptet API
- `HebloShoptetAdapterModule` - ABP modul

### Synchronizace
- **Unidirectional sync** - Data se synchronizují z Shoptet do Anela.Heblo
- **Webhook-based** - Real-time notifikace o změnách
- **Scheduled sync** - Pravidelné synchronizace

## Konfigurace

### appsettings.json
```json
{
  "Shoptet": {
    "ApiUrl": "https://api.shoptet.cz",
    "ApiKey": "your-api-key",
    "WebhookSecret": "webhook-secret",
    "SyncInterval": "00:05:00"
  }
}
```

## API Endpoints

### Produkty
- `GET /api/products` - Seznam produktů
- `GET /api/products/{id}` - Detail produktu

### Objednávky
- `GET /api/orders` - Seznam objednávek
- `GET /api/orders/{id}` - Detail objednávky

### Zákazníci
- `GET /api/customers` - Seznam zákazníků

---

*Tento dokument bude doplněn o konkrétní implementační detaily.* 