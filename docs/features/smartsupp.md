# Smartsupp — integrace zákaznické podpory

## Účel a obchodní kontext

Integrace Smartsupp propojuje live chat platformu Smartsupp s interním systémem Anela Heblo. Konverzace a zprávy ze Smartsupp se automaticky synchronizují do lokální databáze a jsou dostupné v přehledovém rozhraní pro zákaznickou podporu.

### Hlavní funkce

- **Příjem webhooků** — real-time aktualizace konverzací a zpráv ze Smartsupp
- **Manuální synchronizace** — záložní dotažení konverzací přes REST API
- **Přehled konverzací** — zobrazení otevřených/uzavřených konverzací s detailem zpráv
- **AI návrh odpovědi** — agent vygeneruje návrh odpovědi z celé konverzace a databáze znalostí (KnowledgeBase RAG), s volitelným tématem

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

Události jsou zpracovávány přes pattern Reaction — každý event má vlastní třídu `ISmartsuppWebhookReaction`.

**Kontaktní události** (upsert kontaktu):

| Událost | Popis |
|---------|-------|
| `contact.created` | Nový kontakt |
| `contact.updated` | Změna údajů kontaktu |
| `contact.acquired` | Kontakt získán/přiřazen |
| `contact.banned` | Kontakt zablokován |
| `contact.unbanned` | Blokace kontaktu zrušena |

**Konverzační události** (upsert konverzace):

| Událost | Popis |
|---------|-------|
| `conversation.opened` | Konverzace otevřena |
| `conversation.closed` | Konverzace uzavřena agentem (`close_type`, `agent_id`) |
| `conversation.closed_by_contact` | Konverzace uzavřena zákazníkem |
| `conversation.rated` | Zákazník ohodnotil konverzaci (`rating_value`, `rating_text`) |
| `conversation.agent_assigned` | Agent přiřazen ke konverzaci |
| `conversation.agent_unassigned` | Agent odřazen od konverzace |
| `conversation.agent_joined` | Agent se připojil (no-op) |
| `conversation.agent_left` | Agent odešel (no-op) |

**Konverzace + zpráva** (upsert konverzace i zprávy):

| Událost | Popis |
|---------|-------|
| `conversation.contact_replied` | Zákazník napsal zprávu |
| `conversation.agent_replied` | Agent odpověděl |
| `conversation.bot_replied` | Bot odpověděl |

**Doručení zprávy** (aktualizace stavu doručení):

| Událost | Popis |
|---------|-------|
| `conversation.message_delivered` | Zpráva doručena |
| `conversation.message_delivery_failed` | Doručení zprávy selhalo |

> **Poznámka:** Události `conversation.created`, `conversation.updated` a `message.created` **nejsou zpracovávány** — vrátí `200 OK` ale bez akce. Smartsupp místo toho posílá `conversation.opened`, `conversation.contact_replied` apod.

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
| `POST` | `/api/smartsupp/conversations/{id}/draft-reply` | Vygeneruje AI návrh odpovědi (`{ "topic": "Reklamace" }` — volitelné) |
| `GET` | `/api/smartsupp/conversations/{id}/shoptet-info` | Vrátí profil Shoptet zákazníka a poslední objednávky pro danou konverzaci. Vrátí 404 pokud nelze zákazníka identifikovat. |

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

### AI návrh odpovědi

Systémový prompt pro generování návrhů odpovědí má výchozí hodnotu v kódu
(`SmartsuppDraftReplyOptions`). Lze ho přepsat volitelnou sekcí
`SmartsuppDraftReply:DraftReplySystemPrompt` v `appsettings.json`.

Retrieval kontextu probíhá přes KnowledgeBase modul (`SearchDocumentsRequest`).
Dotaz se odvodí z tématu (`topic`), nebo — pokud téma chybí — z posledních
zpráv zákazníka.

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
