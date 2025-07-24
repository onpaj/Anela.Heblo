# Anela Heblo - Frontend

Standalone React PWA aplikace pro kosmetickou společnost Anela Heblo.

## Architektura

- **Framework**: React 18 s TypeScript
- **Styling**: Tailwind CSS podle UI design document
- **Routing**: React Router DOM
- **Internationalization**: i18next (primárně česky)
- **Authentication**: MSAL pro MS Entra ID
- **State Management**: React hooks (rozšíření plánováno podle potřeby)

## Spuštění pro vývoj

```bash
# Instalace závislostí
npm install

# Spuštění dev server s hot reload (localhost:3000)
npm start

# Spuštění testů
npm test

# Build pro produkci
npm run build

# Linting
npm run lint
```

## Konfigurace

Zkopírujte `.env.example` do `.env` a nastavte správné hodnoty:

```bash
cp .env.example .env
```

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

## Development Notes

- Aplikace je připravena pro PWA
- CORS je nakonfigurováno pro komunikaci s backend API na localhost:5000
- Hot reload je aktivní během vývoje
- Pro produkci se aplikace builduje do statických souborů