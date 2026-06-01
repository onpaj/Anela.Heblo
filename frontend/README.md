# Anela Heblo - Frontend

Standalone React PWA aplikace pro kosmetickou společnost Anela Heblo.

## Architektura

- **Framework**: React 18 s TypeScript
- **Styling**: Tailwind CSS podle UI design document
- **Icons**: Lucide React (pro konzistentní a moderní ikony)
- **Routing**: React Router DOM
- **Internationalization**: i18next (primárně česky)
- **Authentication**: MSAL pro MS Entra ID
- **State Management**: React hooks (rozšíření plánováno podle potřeby)

## Spuštění pro vývoj

```bash
# Instalace závislostí
npm install

# Development (localhost:3000 -> backend localhost:8080)
npm start

# Test environment (localhost:44329 -> backend localhost:44388)
npm run start:test

# Production environment (localhost:44330 -> backend localhost:44389)
npm run start:prod

# Spuštění testů
npm test

# Build pro různá prostředí
npm run build        # Development build
npm run build:test   # Test environment build
npm run build:prod   # Production build

# Linting
npm run lint
```

## Konfigurace

### Autentizace (povinné pro lokální vývoj)

1. Zkopírujte `.env.example` do `.env`:
```bash
cp .env.example .env
```

2. Vyplňte skutečné hodnoty Microsoft Entra ID:
   - `REACT_APP_AZURE_CLIENT_ID` - Client ID aplikace v Entra ID
   - `REACT_APP_AZURE_TENANT_ID` - Tenant ID organizace
   - Pro získání hodnot kontaktujte vlastníka projektu

3. **DŮLEŽITÉ**: Soubor `.env` obsahuje citlivé údaje a NESMÍ být commitován do gitu (je v .gitignore)

### Bez autentizace
Aplikace nebude fungovat správně bez validních přihlašovacích údajů pro Microsoft Entra ID.

## Struktura projektu

```
src/
├── components/
│   ├── Layout/          # Sidebar, TopBar, Layout
│   └── pages/           # Stránkové komponenty (prázdné)
├── services/            # API klient (bude nahrazen OpenAPI)
├── auth/                # MSAL konfigurace
├── test/                # Testy
└── i18n.ts             # Internationalization setup
```

## Design System

Aplikace používá Tailwind CSS s custom konfigurací podle UI design document:
- Barvy: Gray paleta s indigo akcenty
- Typography: Systémové fonty
- Layout: Sidebar (w-64) + main content area
- Responsivita: Mobile-first přístup

## Port Configuration

| Environment | Frontend Port | Backend Port |
|-------------|---------------|-------------|
| Development | 3000 | 5000 |
| Test | 44329 | 44388 |
| Production | 44330 | 44389 |

## Development Notes

- Aplikace je připravena pro PWA
- CORS je nakonfigurováno pro komunikaci s backend API
- Hot reload je aktivní během vývoje
- Pro produkci se aplikace builduje do statických souborů
- Různé scripty pro různá prostředí s automatickou konfigurací portů