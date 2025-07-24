# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a documentation repository for "Anela Heblo" - a cosmetics company workspace application. The repository contains comprehensive architecture documentation and design specifications for a full-stack web application that will be built later.

## Architecture Summary

**Stack**: Monorepo (.NET 8 + React), Docker-based deployment
- **Frontend**: Standalone React PWA with i18next localization, MSAL (MS Entra ID) authentication, hot reload support
- **Backend**: ASP.NET Core (.NET 8) REST API with Hangfire background jobs
- **Database**: PostgreSQL with EF Core migrations
- **Authentication**: MS Entra ID (OIDC) with claims-based roles
- **Integrations**: ABRA Flexi (custom API client), Shoptet (Playwright-based scraping)
- **Testing**: Playwright for both E2E testing and Shoptet integration automation
- **Deployment**: Docker containers, GitHub Actions CI/CD

## Repository Structure (Planned)

Based on the infrastructure document, the intended structure will be:
```
/                    # Monorepo root
├── backend/         # ASP.NET Core application
│   ├── src/         # Application code (Api, Domain, Infrastructure, API.Client)
│   │   ├── Anela.Heblo.API/           # Main API project
│   │   ├── Anela.Heblo.API.Client/    # Auto-generated OpenAPI client
│   │   ├── Anela.Heblo.Application/   # Application layer
│   │   ├── Anela.Heblo.Domain/        # Domain models
│   │   └── Anela.Heblo.Infrastructure/ # Infrastructure layer
│   ├── test/        # Unit/integration tests
│   ├── migrations/  # EF Core database migrations
│   └── scripts/     # Utility scripts
├── frontend/        # Standalone React PWA
│   ├── public/      # Static assets
│   ├── src/
│   │   ├── components/
│   │   ├── pages/
│   │   └── services/    # OpenAPI client (generated)
│   ├── test/        # Frontend tests
│   └── package.json # Node.js dependencies
├── .github/         # GitHub Actions workflows
└── docker-compose.yml
```

## Core Modules

1. **Catalog Module**: Unifies product/material data from Shoptet (products) and ABRA (materials)
2. **Manufacture Module**: 2-step production workflow (Materials → Semi-products → Products)  
3. **Purchase Module**: Material shortage detection with supplier/pricing history
4. **Transport Module**: Box-level packaging tracking with EAN codes
5. **Invoice Automation**: Automated Shoptet invoice scraping → ABRA Flexi integration

## Development Commands (When Implemented)

Since this is currently documentation-only, these are the expected commands based on the architecture:

**Backend (.NET 8)**:
- `dotnet build` - Build the solution (automatically generates TypeScript client for frontend in Debug mode)
- `dotnet test` - Run unit tests
- `dotnet ef migrations add <name>` - Create new migration
- `dotnet ef database update` - Apply migrations
- `dotnet run` - Start development server
- `dotnet build --target GenerateFrontendClientManual` - Manually generate TypeScript client for frontend

**Frontend (Standalone React)**:
- `npm install` - Install dependencies
- `npm start` - Start development server with hot reload (localhost:3000)
- `npm test` - Run tests with Jest/React Testing Library
- `npm run build` - Build static files for production deployment
- `npm run lint` - Run linter
- `npx playwright test` - Run end-to-end tests (when configured)
- `npx playwright codegen localhost:3000` - Generate test code by recording interactions

**Authentication Setup** (Required for local development):
1. Copy `frontend/.env.example` to `frontend/.env`
2. Fill in actual Microsoft Entra ID credentials (client ID, tenant ID)
3. The `.env` file is gitignored and contains sensitive data - never commit it
4. Contact project owner for actual credential values

**Docker**:
- `docker-compose up` - Start local development environment
- `docker build -t anela-heblo .` - Build production image (includes frontend static files)

## UI Design System

The frontend follows a Tailwind CSS-based design system with:
- **Layout**: Foldable sidebar navigation + main content area
- **Sidebar**: 
  - **Expanded**: `w-64` (256px) with full navigation and text labels
  - **Collapsed**: `w-16` (64px) with icons only and tooltips
  - **Toggle**: Via button in sidebar bottom (next to user profile)
  - **Button location**: Bottom of sidebar - right side when expanded, center when collapsed
  - **Icons**: `PanelLeftClose` (collapse) / `PanelLeftOpen` (expand)
  - **Animation**: Smooth `transition-all duration-300` for width changes
  - **Content adaptation**: Main content adapts with `md:pl-64` or `md:pl-16`
- **Colors**: Gray-based palette with indigo accents, emerald success states
- **Icons**: Lucide React for consistent, modern iconography
- **Typography**: System fonts with defined hierarchy (XL headings to XS captions)
- **Components**: Consistent buttons, forms, tables with hover states
- **Responsiveness**: Mobile-first approach with sidebar collapsing on `md:` breakpoint
- **Localization**: Czech language primary, i18next framework

## OpenAPI Client Generation

The backend automatically generates a TypeScript client for the React frontend:

- **Source**: API project PostBuild event in Debug mode
- **Configuration**: `backend/src/Anela.Heblo.API/nswag.frontend.json`
- **Output**: `frontend/src/api/generated/api-client.ts`
- **Tool**: NSwag with Fetch API template
- **Integration**: TanStack Query for data fetching and caching
- **Usage**: React hooks in `frontend/src/api/hooks.ts`

### Example Usage:
```typescript
import { useWeatherQuery } from '../api/hooks';

const WeatherComponent = () => {
  const { data, isLoading, error } = useWeatherQuery();
  
  if (isLoading) return <div>Loading...</div>;
  if (error) return <div>Error: {error.message}</div>;
  
  return <div>{/* Render weather data */}</div>;
};
```

## Background Jobs (Hangfire)

- **Stock Sync**: Refresh catalog every 10 minutes
- **Invoice Sync**: Pull Shoptet invoices → push to ABRA
- **Transport Sync**: Confirm EANs and update Shoptet stock
- **Batch Planning**: Periodic manufacturing evaluation

## Environment Configuration

- Uses `.env` files for shared configuration
- Frontend variables prefixed with `REACT_APP_`
- Backend uses `appsettings.{Environment}.json` + environment variables
- Database migrations applied manually (not automated in CI/CD)

## Port Configuration

| Environment | Frontend Port | Backend Port | Usage |
|-------------|---------------|--------------|-------|
| Development | 3000 | 5000 | Internal development, Playwright testing |
| Manual Debug | 3001 | 5000 | VS Code launch.json manual debugging |
| Test | 44329 | 44388 | Test environment |
| Production | 44330 | 44389 | Production environment |

## Deployment Strategy

- **Development**: 
  - Frontend: Standalone React dev server (`npm start`) with hot reload (localhost:3000)
  - Backend: ASP.NET Core dev server (`dotnet run`) (localhost:5000)
  - CORS configured to allow frontend-backend communication
- **Test Environment**:
  - Frontend: localhost:44329
  - Backend: localhost:44388
- **Production Environment**:
  - Frontend: localhost:44330  
  - Backend: localhost:44389
- **Current**: Docker on-premises (Synology NAS)
- **Future**: Azure App Service / Container Apps
- **Versioning**: Semantic versioning with conventional commits
- **CI/CD**: GitHub Actions with feature branch testing, main branch auto-deploy

## Design Document Alignment Rules

**MANDATORY**: All implementation work MUST align with the application design documents in `/docs`. Before making ANY changes to code, architecture, or design, Claude Code MUST:

### 1. Consultation Requirements

**Before ANY implementation work:**
- Read and understand relevant design documents from `/docs`
- Verify proposed changes align with documented architecture
- If conflicts arise, ask for clarification rather than making assumptions

**Required documents for different change types:**

- **Backend/API changes**: Consult `docs/📘 Architecture Documentation – MVP Work.md` for module definitions and data flow
- **Infrastructure/deployment changes**: Consult `docs/application_infrastructure.md` for deployment strategy and CI/CD rules  
- **Frontend/UI changes**: Consult `docs/ui_design_document.md` for design system, colors, typography, and component specifications
- **Any architectural decisions**: Consult ALL documents to ensure consistency

### 2. Alignment Verification

Before implementing, Claude Code MUST:
- Explicitly state which design document(s) were consulted
- Confirm the implementation follows documented patterns
- Identify any deviations and justify them or seek approval

### 3. Documentation Updates

**CRITICAL**: Whenever architectural changes are agreed upon, the following documentation must be updated immediately:
- `docs/📘 Architecture Documentation – MVP Work.md` - Core architecture and module definitions
- `docs/application_infrastructure.md` - Infrastructure, deployment, and CI/CD details  
- `docs/ui_design_document.md` - UI/UX specifications and design system
- `CLAUDE.md` - This file for future Claude Code instances

This ensures documentation stays synchronized with actual implementation and architectural decisions.

### 4. Enforcement

- **NO implementation without consultation** - All code changes must reference appropriate design documents
- **NO architectural deviations without approval** - Stay within documented patterns unless explicitly asked to change them
- **Documentation-first approach** - When in doubt, follow the documentation; ask for updates if needed

## Frontend Development & Testing Rules

### Playwright Integration

**MANDATORY**: When developing, testing, or iterating on frontend components, Claude Code MUST use Playwright for:

1. **Visual Testing & Validation**:
   - Use `npx playwright codegen localhost:3000` to record user interactions
   - Port 3000 is reserved for internal development and Playwright testing
   - Verify UI changes work correctly across different screen sizes
   - Test responsive behavior (mobile, tablet, desktop breakpoints)
   - Validate component states (hover, active, disabled, etc.)

2. **Interactive Development**:
   - Test complex user flows (sidebar collapse/expand, form submissions, navigation)
   - Verify accessibility features (keyboard navigation, screen reader compatibility)
   - Validate cross-browser compatibility when needed

3. **Regression Testing**:
   - After significant UI changes, run tests to ensure existing functionality still works
   - Test component interactions and state management
   - Verify responsive design adaptations

4. **When to Use Playwright**:
   - **Required**: Major layout changes, new component implementations
   - **Required**: Responsive design updates or sidebar behavior changes  
   - **Required**: Form interactions, navigation flows, or state-dependent UI
   - **Optional**: Minor styling tweaks or text changes

5. **Playwright Commands**:
   - `npx playwright install` - Install browsers (run once)
   - `npx playwright codegen localhost:3000` - Record interactions for testing
   - `npx playwright test` - Run existing test suite
   - `npx playwright test --headed` - Run tests with visible browser
   - `npx playwright show-report` - View test results

### Development Workflow

1. **Make UI changes** to React components
2. **Start dev server** with `npm start`
3. **Use Playwright** to record and validate interactions
4. **Test responsive behavior** across breakpoints
5. **Verify accessibility** and keyboard navigation
6. **Run build** to ensure no compilation errors

## Security Rules for Credentials & Secrets

### CRITICAL: Credentials Security

**NEVER commit credentials to source control:**
- ❌ No real passwords, API keys, or tokens in any committed files
- ❌ No hardcoded credentials in test files
- ❌ No authentication secrets in configuration files

**Required approach for test credentials:**
- ✅ Use local `.env.test` files (always gitignored)
- ✅ Load credentials via secure utility functions like `loadTestCredentials()`
- ✅ Provide clear setup instructions for local credential files
- ✅ Exit with error if credentials file is missing

**Test credentials setup:**
```bash
# Create local file (NEVER commit):
frontend/test/auth/.env.test

# Content:
TEST_EMAIL=your_email@domain.com
TEST_PASSWORD=your_password
```

**Example secure credential loading:**
```javascript
const { loadTestCredentials } = require('./test-credentials');
const credentials = loadTestCredentials(); // Safe local loading
await emailInput.fill(credentials.email); // Never hardcoded
```

### Enforcement
- All credential files are in `.gitignore`
- Tests fail if credentials are missing
- Code review must catch any hardcoded secrets

## Git Workflow Rules

- **NO automatic commits** - Claude Code should never create git commits automatically
- **Auto-accept file changes** - Claude Code can automatically stage and accept file modifications
- **Manual commit control** - All commits are made manually by the developer
- This ensures full control over commit timing, messages, and change grouping

## Important Notes

- This is a **solo developer project** with AI-assisted PR reviews
- Database migrations are **manual** - not part of automated deployment  
- EF Core is used for database access and migrations
- OpenAPI client generation for frontend (post-build step)
- All Docker images pushed to Docker Hub
- Observability via Application Insights