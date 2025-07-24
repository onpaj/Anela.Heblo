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
- **Deployment**: Docker containers, GitHub Actions CI/CD

## Repository Structure (Planned)

Based on the infrastructure document, the intended structure will be:
```
/                    # Monorepo root
├── backend/         # ASP.NET Core application
│   ├── src/         # Application code (Api, Domain, Infrastructure)
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
- `dotnet build` - Build the solution
- `dotnet test` - Run unit tests
- `dotnet ef migrations add <name>` - Create new migration
- `dotnet ef database update` - Apply migrations
- `dotnet run` - Start development server

**Frontend (Standalone React)**:
- `npm install` - Install dependencies
- `npm start` - Start development server with hot reload (typically localhost:3000)
- `npm test` - Run tests with Jest/React Testing Library
- `npm run build` - Build static files for production deployment
- `npm run lint` - Run linter

**Docker**:
- `docker-compose up` - Start local development environment
- `docker build -t anela-heblo .` - Build production image (includes frontend static files)

## UI Design System

The frontend follows a Tailwind CSS-based design system with:
- **Layout**: Sidebar navigation (`w-64`) + main content area
- **Colors**: Gray-based palette with indigo accents, emerald success states
- **Icons**: Lucide React for consistent, modern iconography
- **Typography**: System fonts with defined hierarchy (XL headings to XS captions)
- **Components**: Consistent buttons, forms, tables with hover states
- **Responsiveness**: Mobile-first approach with sidebar collapsing on `md:` breakpoint
- **Localization**: Czech language primary, i18next framework

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

| Environment | Frontend Port | Backend Port |
|-------------|---------------|--------------|
| Development | 3000 | 5000 |
| Test | 44329 | 44388 |
| Production | 44330 | 44389 |

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