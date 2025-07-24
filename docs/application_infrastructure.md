# 📘 Application Infrastructure Design Document

> **Stack**: Monorepo (`.NET 8` + `React`), Docker-based deployment (on-prem Synology NAS, future Azure), GitHub + GitHub Actions, EF Core, Docker Hub.

---

## 1. 📁 Directory Structure

```
/                  # Monorepo root
├── backend/       # Backend – ASP.NET Core application
│   ├── src/       # Application code
│   │   ├── Anela.Heblo.API/           # Main API project
│   │   ├── Anela.Heblo.API.Client/    # Auto-generated OpenAPI client
│   │   ├── Anela.Heblo.Application/   # Application layer
│   │   ├── Anela.Heblo.Domain/        # Domain models
│   │   └── Anela.Heblo.Infrastructure/ # Infrastructure layer
│   ├── test/      # Unit/integration tests for backend
│   ├── migrations/ # EF Core database migrations
│   ├── scripts/   # Utility scripts (e.g. DB tools, backups)
│   └── Dockerfile
│
├── frontend/      # Standalone React PWA
│   ├── public/     # Static assets (index.html, favicon, etc.)
│   ├── src/
│   │   ├── components/
│   │   ├── pages/
│   │   ├── services/    # OpenAPI client (generated)
│   │   └── ...
│   ├── test/       # Frontend tests (Jest, React Testing Library)
│   └── package.json # Node.js dependencies and scripts
│
├── .github/        # GitHub Actions workflows
├── .env            # Dev environment variables
└── docker-compose.yml # For local dev/test if needed
```

---

## 2. 🌍 Environment Definition

- `.env` file in project root defines shared configuration.
- Frontend uses variables prefixed with `REACT_APP_`.
- Backend uses `appsettings.{Environment}.json` + environment variables.

### Port Configuration:

| Environment | Frontend Port | Backend Port |
|-------------|---------------|--------------|
| Development | 3000 | 5000 |
| Test | 44329 | 44388 |
| Production | 44330 | 44389 |

### Example `.env`:

```
# Development
ASPNETCORE_ENVIRONMENT=Development
REACT_APP_API_URL=http://localhost:5000
API_BASE_URL=http://localhost:5000

# Test Environment
ASPNETCORE_ENVIRONMENT=Test
REACT_APP_API_URL=http://localhost:44388
API_BASE_URL=http://localhost:44388

# Production
ASPNETCORE_ENVIRONMENT=Production
REACT_APP_API_URL=http://localhost:44389
API_BASE_URL=http://localhost:44389
```

---

## 3. 🔁 CI/CD Rules & Workflow

- **CI runs on all branches**: build, lint, tests.
- **Feature branches**:
  - Optional deployment to `test` environment via GitHub Actions manual trigger.
- **Main branch**:
  - Merge allowed only if CI succeeds.
  - Automatic deployment to production environment (NAS, future Azure).

### Deployment Architecture:

- **Development**: 
  - Frontend: Standalone React dev server (`npm start`) with hot reload on localhost:3000
  - Backend: ASP.NET Core dev server (`dotnet run`) on localhost:5000
  - CORS configured for cross-origin requests between frontend and backend
- **Test Environment**:
  - Frontend: React dev server on localhost:44329
  - Backend: ASP.NET Core on localhost:44388
- **Production**: 
  - Frontend: Static files on localhost:44330
  - Backend: ASP.NET Core API on localhost:44389
  - Two separate deployments but coordinated via CI/CD
- All Docker images are pushed to **Docker Hub**.
- Deployment is implemented via GitHub Actions (defined later).
- `.env`-based secrets are used for now.

---

## 4. 🗃️ Database Versioning

- Database migrations are managed via **EF Core**.
- Migrations are stored in `backend/migrations`.
- Migration naming convention: `AddXyzTable`, `AddIndexToProductName`, etc.
- **Migrations are applied manually** – not part of automated CI/CD.

---

## 5. 🌿 Branching Strategy

- **Main branch** is the releasable production code.
- **Feature branches**: `feature/*`
- **Bugfix branches**: `fix/*`
- Merge to `main`:
  - Must pass CI pipeline
  - Uses **merge commit** (squash in PR, merge = merge commit)
- **AI PR reviewer** is used to validate PRs (solo developer setup)

---

## 6. 🔖 Project Versioning

- Follows **Semantic Versioning**: `MAJOR.MINOR.PATCH`
- Version bump is done automatically based on **conventional commit messages**
  - e.g. `feat:`, `fix:`, `chore:`, etc.
- Version is included in:
  - Docker image tags
  - `AssemblyInfo.cs`
  - `package.json`
- Tagging `vX.Y.Z` on `main` triggers production release

---

## 7. 🔧 OpenAPI Client Generation

### Backend C# Client

- **Location**: `backend/src/Anela.Heblo.API.Client/`
- **Auto-generation**: PostBuild event in API project (Debug mode only)
- **Tool**: NSwag with System.Text.Json
- **Output**: `Generated/AnelaHebloApiClient.cs`
- **Manual Generation**: Scripts available (`generate-client.ps1`, `generate-client.sh`)

### Frontend TypeScript Client

- **Location**: `frontend/src/services/generated/api-client.ts`
- **Auto-generation**: Via backend PostBuild event or frontend prebuild script
- **Tool**: NSwag with Fetch API template
- **Manual Generation**: `npm run generate-client` in frontend directory
- **Build Integration**: Automatically generated before frontend build (`prebuild` script)

---

## 8. 🧠 AI Review Agent

- Since the project is developed by a single developer:
  - PR review is handled by an **AI agent**.
  - PRs must include CI success and optionally auto-reviewed comments.

---

## ✅ Summary

This document defines the project’s infrastructure practices and expectations:

- Clean monorepo layout
- Clearly separated `test` and `production` environments
- Consistent versioning and release triggers
- GitHub Actions-based automation with optional test deploys
- Manual database management
- AI-assisted code reviews

