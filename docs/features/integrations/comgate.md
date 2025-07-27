# Comgate Integrace

> **Status**: Plánováno  
> **Poslední aktualizace**: $(Get-Date -Format "dd.MM.yyyy")

## Přehled

Comgate je platební brána, se kterou Anela.Heblo.Blazor synchronizuje:
- Bankovní výpisy
- Platební transakce
- Refundace a stornace
- Reporting a analýzy

## Architektura integrace

### Adapter Pattern
- `Anela.Heblo.Adapters.Comgate` - Hlavní adapter
- `ComgateBankClient` - Klient pro Comgate API
- `ComgateSettings` - Konfigurace

### Synchronizace
- **Daily import** - Denní import bankovních výpisů
- **Real-time notifications** - Webhook notifikace
- **Manual import** - Ruční import dat

## Konfigurace

### appsettings.json
```json
{
  "Comgate": {
    "ApiUrl": "https://payments.comgate.cz",
    "MerchantId": "your-merchant-id",
    "ApiKey": "your-api-key",
    "TestMode": false
  }
}
```

## API Endpoints

### Bankovní výpisy
- `GET /api/statements` - Seznam výpisů
- `POST /api/statements/import` - Import výpisu

### Transakce
- `GET /api/transactions` - Seznam transakcí
- `GET /api/transactions/{id}` - Detail transakce

---

*Tento dokument bude doplněn o konkrétní implementační detaily.* 