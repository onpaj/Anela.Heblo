# Smartsupp — integrace zákaznické podpory

## Účel a obchodní kontext

Integrace Smartsupp propojuje live chat platformu Smartsupp s interním systémem Anela Heblo. Konverzace a zprávy ze Smartsupp se automaticky synchronizují do lokální databáze a jsou dostupné v přehledovém rozhraní pro zákaznickou podporu.

### Hlavní funkce

- **Příjem webhooků** — real-time aktualizace konverzací a zpráv ze Smartsupp
- **Manuální synchronizace** — záložní dotažení konverzací přes REST API
- **Přehled konverzací** — zobrazení otevřených/uzavřených konverzací s detailem zpráv

---

## Architektura

### Tok dat

```
Smartsupp → POST /api/webhooks/smartsupp → SmartsuppWebhookController
                                          → ProcessWebhookEventHandler
                                          → SmartsuppRepository (upsert)

Hangfire scheduler / uživatel → POST /api/smartsupp/sync → RunManualSyncHandler
                                                          → Smartsupp REST API
                                                          → SmartsuppRepository (upsert)
```

### Podpořené webhook události

| Událost | Popis |
|---------|-------|
| `conversation.created` | Nová konverzace |
| `conversation.updated` | Změna stavu/přiřazení konverzace |
| `conversation.closed` | Konverzace uzavřena |
| `message.created` | Nová zpráva (contact / agent / bot) |

---

## API endpointy

### Webhook (veřejný)

| Metoda | Cesta | Popis |
|--------|-------|-------|
| `POST` | `/api/webhooks/smartsupp` | Přijímá webhook události ze Smartsupp |

Ověření identity probíhá přes HMAC-SHA256 hlavičku `X-Smartsupp-Hmac`. Viz [Postman průvodce testováním](../integrations/smartsupp-webhook.md).

### Konverzace (chráněné — vyžaduje přihlášení)

| Metoda | Cesta | Popis |
|--------|-------|-------|
| `GET` | `/api/smartsupp/conversations` | Seznam konverzací (`?status=Open\|Resolved&page=1&pageSize=50`) |
| `GET` | `/api/smartsupp/conversations/{id}` | Detail konverzace |
| `POST` | `/api/smartsupp/sync` | Spustí manuální synchronizaci (`{ "since": "2026-01-01T00:00:00Z" }` — volitelné) |

---

## Konfigurace

Nastavení v `appsettings.json` (citlivé hodnoty v `secrets.json`):

```json
"Smartsupp": {
  "ApiToken": "-- secrets.json --",
  "BaseUrl": "https://api.smartsupp.com/v2/",
  "WebhookSecret": "-- secrets.json --",
  "WebhookAppId": "",
  "HttpTimeoutSeconds": 30
}
```

| Klíč | Popis |
|------|-------|
| `ApiToken` | Bearer token pro Smartsupp REST API (manuální sync) |
| `WebhookSecret` | Sdílené tajemství pro ověření HMAC podpisu webhooků |
| `WebhookAppId` | Pokud je nastaven, přijímá pouze webhooky se shodným `app_id`; prázdný = přijímá vše |

---

## Frontend

Stránka zákaznické podpory je dostupná v sekci **Customer Support → Smartsupp Chats**.

Funkce:
- Přepínání mezi otevřenými a uzavřenými konverzacemi
- Detail konverzace se zprávami
- Tlačítko **Sync now** pro ruční synchronizaci

---

## Externí zdroje

| Zdroj | Odkaz |
|-------|-------|
| Smartsupp OpenAPI specifikace (v2) | https://api.smartsupp.com/v2/specs/open-api.json |
| Webhook definice a události | https://docs.smartsupp.com/rest-api/webhooks |
| Postman průvodce testováním webhooků | [docs/integrations/smartsupp-webhook.md](../integrations/smartsupp-webhook.md) |
