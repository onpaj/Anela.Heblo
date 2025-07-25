# 📘 Application Infrastructure Design Document

> **Stack**: Monorepo (`.NET 8` + `React`), Single Docker image deployment, Azure Web App for Containers, GitHub + GitHub Actions, EF Core, Docker Hub.

---

## 1. 📁 Directory Structure

> **Note**: Detailed filesystem structure documentation has been moved to [`filesystem.md`](./filesystem.md).

---

## 2. 🌍 Environment Definition

> **Note**: Detailed environment configuration documentation has been moved to [`environments.md`](./environments.md).


---

## 3. 🔁 CI/CD Rules & Workflow

- **CI runs on all branches**: build, lint, tests, Docker image build.
- **Feature branches**:
  - Optional deployment to `test` environment via GitHub Actions manual trigger.
- **Main branch**:
  - Merge allowed only if CI succeeds.
  - Automatic deployment to production Azure Web App for Containers.

### Deployment Architecture:

> **Note**: Detailed deployment architecture is documented in [`environments.md`](./environments.md).
- Docker images are pushed to **Docker Hub** with semantic versioning tags.
- Deployment implemented via **GitHub Actions** to Azure Web App for Containers.
- Environment-specific configuration via Azure App Settings.
- **CI validation removed**: Deployment workflows no longer wait for CI to pass (as of current implementation).

---

## 4. 🐳 Docker Build Strategy

### Single Image Architecture:
- **Base Image**: `mcr.microsoft.com/dotnet/aspnet:8.0` (production) / `mcr.microsoft.com/dotnet/sdk:8.0` (build)
- **Multi-stage build**:
  1. **Frontend Build Stage**: Node.js to build React app (`npm run build`)
  2. **Backend Build Stage**: .NET SDK to build ASP.NET Core app
  3. **Runtime Stage**: Copy built React files to `wwwroot/` and ASP.NET Core binaries
- **Final Container**: ASP.NET Core serves React static files + API endpoints
- **Port**: Container exposes port `80` (internal), Azure maps to `443` (HTTPS)

### Build Process:
```dockerfile
# Multi-stage Dockerfile example structure:
FROM node:18 AS frontend-build
# Build React app -> /app/build/

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS backend-build  
# Build .NET app -> /app/publish/

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
# Copy React build to wwwroot/
# Copy .NET app
# Configure to serve static files + API
```

### Static File Serving (Test/Production only):
- **Development**: NOT used - React dev server handles all frontend serving with hot reload
- **Test/Production**: ASP.NET Core configured with `app.UseStaticFiles()` and `app.UseSpaStaticFiles()`
- React Router handled via fallback to `index.html`
- API routes prefixed (e.g., `/api/*`, `/WeatherForecast`)
- Frontend routes handled by React Router

### Development vs Production Architecture:
```
Development:           Production/Test:
┌─────────────┐       ┌─────────────────────┐
│React Dev    │       │Single Container     │
│Server :3000 │ CORS  │┌─────────┬─────────┐│
│(Hot Reload) │◄─────►││React    │ASP.NET  ││
└─────────────┘       ││Static   │Core API ││
                      ││Files    │:80      ││
┌─────────────┐       │└─────────┴─────────┘│
│ASP.NET Core │       │Azure Web App        │
│API :44390   │       └─────────────────────┘
└─────────────┘
```

---

## 5. 🗃️ Database Versioning

- Database migrations are managed via **EF Core**.
- Migrations are stored in `backend/migrations`.
- Migration naming convention: `AddXyzTable`, `AddIndexToProductName`, etc.
- **Migrations are applied manually** – not part of automated CI/CD.

---

## 6. 🚀 Azure Web App for Containers Deployment

> **Note**: Azure Web App deployment configuration is documented in [`environments.md`](./environments.md).

---

## 7. 🌿 Branching Strategy

- **Main branch** is the releasable production code.
- **Feature branches**: `feature/*`
- **Bugfix branches**: `fix/*`
- Merge to `main`:
  - Must pass CI pipeline
  - Uses **merge commit** (squash in PR, merge = merge commit)
- **AI PR reviewer** is used to validate PRs (solo developer setup)

---

## 8. 🔖 Project Versioning

- Follows **Semantic Versioning**: `MAJOR.MINOR.PATCH`
- Version bump is done automatically based on **conventional commit messages**
  - e.g. `feat:`, `fix:`, `chore:`, etc.
- Version is included in:
  - Docker image tags
  - `AssemblyInfo.cs`
  - `package.json`
- Tagging `vX.Y.Z` on `main` triggers production release

---

## 9. 🧠 AI Review Agent

- Since the project is developed by a single developer:
  - PR review is handled by an **AI agent**.
  - PRs must include CI success and optionally auto-reviewed comments.

---

## 10. 🔐 GitHub Secrets Configuration

### Required Secrets for CI/CD Pipeline

The following secrets must be configured in GitHub repository settings (Settings → Secrets and variables → Actions):

#### 🐳 Docker Hub Authentication
- **`DOCKER_USERNAME`**: Your Docker Hub username
- **`DOCKER_PASSWORD`**: Docker Hub access token (not password!)
  - Create at: Docker Hub → Account Settings → Security → New Access Token
  - Recommended permissions: Read, Write, Delete

#### ☁️ Azure Service Principal Credentials
- **`AZURE_CREDENTIALS_TEST`**: Service principal for test environment
- **`AZURE_CREDENTIALS_PROD`**: Service principal for production environment
  
  Create with Azure CLI:
  ```bash
  # Test environment
  az ad sp create-for-rbac \
    --name "github-actions-anela-heblo-test" \
    --role contributor \
    --scopes /subscriptions/{subscription-id}/resourceGroups/rg-anela-heblo-test \
    --sdk-auth

  # Production environment  
  az ad sp create-for-rbac \
    --name "github-actions-anela-heblo-prod" \
    --role contributor \
    --scopes /subscriptions/{subscription-id}/resourceGroups/rg-anela-heblo-prod \
    --sdk-auth
  ```
  
  The output JSON should be stored as the secret value.

#### 🔑 Microsoft Entra ID (Azure AD) Authentication
- **`AZURE_CLIENT_ID_PROD`**: Azure AD application client ID for production
- **`AZURE_AUTHORITY_PROD`**: Azure AD authority URL
  - Format: `https://login.microsoftonline.com/{tenant-id}`
  - Get from: Azure Portal → App registrations → Your app → Overview

#### 🤖 Automatically Provided
- **`GITHUB_TOKEN`**: Automatically provided by GitHub Actions
  - Has permissions to create releases, push tags, etc.
  - Requires `contents: write` permission in workflow file

### CI/CD Pipeline Configuration

#### Workflow Permissions
Production deployment workflow requires:
```yaml
permissions:
  contents: write  # For creating git tags
  packages: write  # For pushing Docker images
```

#### Environment-Specific App Settings

> **Note**: Environment-specific settings are detailed in [`environments.md`](./environments.md).

---

## ✅ Summary

This document defines the project's infrastructure practices and expectations:

- **Monorepo layout** with hybrid deployment strategy
- **Development**: Separate servers (React dev server + ASP.NET Core) for **hot reload**
- **Test/Production**: Single Docker container on **Azure Web App for Containers**
- **Environment separation**: Development (separate for hot reload) vs Test/Production (single container)
- **Consistent versioning** and automated release triggers
- **GitHub Actions CI/CD** with Docker Hub registry and Azure deployment
- **Mock authentication** for development and test environments
- **Manual database management** with EF Core migrations
- **AI-assisted code reviews** for solo developer workflow

