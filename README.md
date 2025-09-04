# Anela Heblo

Workspace application for cosmetics company operations - a full-stack web application for managing catalog, manufacturing, purchasing, transport, and invoice automation.

## Architecture

- **Frontend**: React PWA with TypeScript, Tailwind CSS, i18next localization
- **Backend**: ASP.NET Core (.NET 8) with Clean Architecture, MediatR pattern
- **Database**: PostgreSQL with EF Core migrations
- **Authentication**: MS Entra ID (production) / Mock Auth (development)
- **Deployment**: Single Docker container on Azure Web App for Containers

## Quick Start

**Development (separate servers with hot reload):**
```bash
# Backend (port 5000)
cd backend/src/Anela.Heblo.API
dotnet run

# Frontend (port 3000)
cd frontend
npm install
npm start
```

**Testing:**
```bash
# Backend tests
dotnet test

# Frontend tests  
npm test

# UI tests (Playwright)
./scripts/run-playwright-tests.sh
```

## Core Modules

- **Catalog**: Unified product/material data from Shoptet & ABRA
- **Manufacture**: 2-step production workflow (Materials → Semi-products → Products)
- **Purchase**: Material shortage detection with supplier history
- **Transport**: Box-level packaging tracking with EAN codes
- **Invoice Automation**: Shoptet invoice scraping → ABRA Flexi integration

## Documentation

- **[📘 Architecture Documentation](docs/📘%20Architecture%20Documentation%20–%20MVP%20Work.md)** - Core architecture and module definitions
- **[Infrastructure](docs/architecture/application_infrastructure.md)** - Deployment, CI/CD, Docker configuration
- **[Environments](docs/architecture/environments.md)** - Port mappings, CORS, Azure settings
- **[Filesystem](docs/architecture/filesystem.md)** - Directory structure and file organization
- **[UI Design Document](docs/ui_design_document.md)** - Design system, components, layout standards
- **[Feature Documentation](docs/features/)** - Feature-specific guides
- **[Task Definitions](docs/tasks/)** - Reusable development tasks

## Technology Stack

| Layer | Technology |
|-------|------------|
| Frontend | React, TypeScript, Tailwind CSS, TanStack Query |
| Backend | ASP.NET Core, MediatR, Hangfire, EF Core |
| Database | PostgreSQL |
| Authentication | MS Entra ID / Mock Auth |
| Deployment | Docker, Azure Web App for Containers |
| CI/CD | GitHub Actions |

## Environment URLs

- **Local Development**: http://localhost:3000 (frontend) + http://localhost:5000 (backend)
- **Local Testing**: http://localhost:3001 (automation environment)
