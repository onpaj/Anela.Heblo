# 📘 Application Infrastructure Design Document

> **Stack**: Monorepo (`.NET 8` + `React`), Single Docker image deployment, Azure Web App for Containers, GitHub + GitHub Actions, EF Core, Docker Hub.

---

## 1. 📁 Directory Structure

```
/                  # Monorepo root
├── backend/       # Backend – ASP.NET Core application
│   ├── src/       # Application code
│   │   ├── Anela.Heblo.API/           # Main API project (serves React app)
│   │   ├── Anela.Heblo.API.Client/    # Auto-generated OpenAPI client
│   │   ├── Anela.Heblo.Application/   # Application layer
│   │   ├── Anela.Heblo.Domain/        # Domain models
│   │   └── Anela.Heblo.Infrastructure/ # Infrastructure layer
│   ├── test/      # Unit/integration tests for backend
│   ├── migrations/ # EF Core database migrations
│   └── scripts/   # Utility scripts (e.g. DB tools, backups)
│
├── frontend/      # React PWA (builds into backend wwwroot)
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
├── Dockerfile      # Single image for backend + frontend
├── docker-compose.yml # For local dev/test if needed
└── .dockerignore   # Docker build optimization
```

---

## 2. 🌍 Environment Definition

- `.env` file in project root defines shared configuration.
- Frontend uses variables prefixed with `REACT_APP_`.
- Backend uses `appsettings.{Environment}.json` + environment variables.

### Port Configuration:

| Environment | Frontend Port | Backend Port | Container Port | Azure URL | Deployment Type |
|-------------|---------------|--------------|----------------|-----------|-----------------|
| **Local Development** | 3000 | 5000 | - | - | Separate servers (hot reload) |
| **Local Automation/Playwright** | 3001 | 5001 | - | - | Separate servers (testing) |
| **Test Environment** | 8080 | 5000 | 8080 | https://heblo-test.azurewebsites.net | Single container |
| **Production** | 8080 | 5000 | 8080 | https://heblo.azurewebsites.net | Single container |

#### Port Details:

- **Local Development**: Frontend dev server (3000) → Backend dev server (5000)
- **Local Automation**: Frontend dev server (3001) → Backend dev server (5001) for Playwright testing
- **Container Environments**: Single container with ASP.NET Core on port 8080 serving both frontend static files and API endpoints
- **Azure Web Apps**: Container port 8080 mapped to standard HTTPS (443) via Azure Load Balancer

### Environment Variables Configuration:

#### **Local Development (.env.local):**
```bash
# Backend runs on port 5000, frontend on 3000
ASPNETCORE_ENVIRONMENT=Development
REACT_APP_API_URL=http://localhost:5000
REACT_APP_USE_MOCK_AUTH=true
```

#### **Local Automation/Playwright (.env.automation):**
```bash
# Backend runs on port 5001, frontend on 3001 for testing isolation
ASPNETCORE_ENVIRONMENT=Development
REACT_APP_API_URL=http://localhost:5001
REACT_APP_USE_MOCK_AUTH=true
```

#### **Azure Test Environment (Azure App Settings):**
```bash
ASPNETCORE_ENVIRONMENT=Test
REACT_APP_API_URL=https://heblo-test.azurewebsites.net
REACT_APP_USE_MOCK_AUTH=true
WEBSITES_PORT=8080
```

#### **Azure Production Environment (Azure App Settings):**
```bash
ASPNETCORE_ENVIRONMENT=Production
REACT_APP_API_URL=https://heblo.azurewebsites.net
REACT_APP_USE_MOCK_AUTH=false
REACT_APP_AZURE_CLIENT_ID=[SET_VIA_SECRETS]
REACT_APP_AZURE_AUTHORITY=[SET_VIA_SECRETS]
WEBSITES_PORT=8080
```

### CORS and Azure AD Redirect URI Configuration

#### CORS Policy Configuration:

**Local Development:**
- **Frontend Origin**: `http://localhost:3000`
- **Backend CORS**: Allow `http://localhost:3000`
- **API Calls**: `http://localhost:3000` → `http://localhost:5000`

**Local Automation/Playwright:**
- **Frontend Origin**: `http://localhost:3001`
- **Backend CORS**: Allow `http://localhost:3001`
- **API Calls**: `http://localhost:3001` → `http://localhost:5001`

**Azure Test Environment:**
- **Origin**: `https://heblo-test.azurewebsites.net` (actual URL may vary with Azure suffix)
- **CORS**: Not needed (same origin - ASP.NET Core serves both static files and API)
- **API Calls**: Internal within ASP.NET Core application

**Azure Production:**
- **Origin**: `https://heblo.azurewebsites.net` (actual URL may vary with Azure suffix)
- **CORS**: Not needed (same origin - ASP.NET Core serves both static files and API)
- **API Calls**: Internal within ASP.NET Core application

#### Azure AD Redirect URI Configuration:

Configure these redirect URIs in Azure Portal → App registrations → Your app → Authentication:

**Local Development:**
- `http://localhost:3000` (for React dev server)
- `http://localhost:3000/auth/callback` (if using callback route)

**Local Automation/Playwright:**
- `http://localhost:3001` (for React dev server during testing)
- `http://localhost:3001/auth/callback` (if using callback route)

**Azure Test Environment:**
- `https://heblo-test.azurewebsites.net`
- `https://heblo-test.azurewebsites.net/auth/callback`
- **Note**: Update with actual Azure-generated URL (e.g., `https://heblo-test-xyz123.azurewebsites.net`)

**Azure Production:**
- `https://heblo.azurewebsites.net`
- `https://heblo.azurewebsites.net/auth/callback`
- **Note**: Update with actual Azure-generated URL (e.g., `https://heblo-xyz123.azurewebsites.net`)

#### Implementation Notes:

1. **Single Origin Strategy**: Container environments use same origin for frontend and backend, eliminating CORS issues
2. **Development CORS**: Only needed for local development with separate servers
3. **Dynamic URLs**: Azure may append random suffixes to web app names - update redirect URIs accordingly
4. **HTTPS Required**: Azure AD requires HTTPS for production redirect URIs
5. **Callback Routes**: Optional - MSAL can handle auth without explicit callback routes

---

## 3. 🔁 CI/CD Rules & Workflow

- **CI runs on all branches**: build, lint, tests, Docker image build.
- **Feature branches**:
  - Optional deployment to `test` environment via GitHub Actions manual trigger.
- **Main branch**:
  - Merge allowed only if CI succeeds.
  - Automatic deployment to production Azure Web App for Containers.

### Deployment Architecture:

- **Development/Debug**: 
  - **Frontend**: Standalone React dev server (`npm start`) with **hot reload** (localhost:3000)
  - **Backend**: ASP.NET Core dev server (`dotnet run`) (localhost:5000)
  - **Architecture**: **Separate servers** to preserve hot reload functionality for development
  - **CORS**: Configured for cross-origin requests between frontend and backend
  - **Authentication**: Mock authentication enabled for both frontend and backend
  - **Why separate**: Hot reload requires React dev server - single container would disable this critical development feature

- **Local Docker Testing**:
  - **Single Docker container** running on port 8080
  - Container serves both React static files and ASP.NET Core API
  - URL: `http://localhost:8080`
  - Used for testing production-like environment locally

- **Azure Test Environment**:
  - **Single Docker container** hosted on **Azure Web App for Containers**
  - **Resource Group**: `rgHeblo`
  - **Web App**: `heblo-test` (Azure may append random suffix)
  - **URL**: `https://heblo-test.azurewebsites.net` (actual URL may vary)
  - **Container Port**: 8080
  - **Authentication**: Mock authentication enabled

- **Azure Production Environment**: 
  - **Single Docker container** hosted on **Azure Web App for Containers**
  - **Resource Group**: `rgHeblo` (shared with test)
  - **Web App**: `heblo` (Azure may append random suffix)
  - **URL**: `https://heblo.azurewebsites.net` (actual URL may vary)
  - **Container Port**: 8080
  - **Authentication**: Azure AD (Microsoft Entra ID)
  - Container serves both React static files and ASP.NET Core API
  - URL: `https://anela-heblo.azurewebsites.net`
  - Real Microsoft Entra ID authentication
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

### Infrastructure:
- **Service**: Azure Web App for Containers (Linux)
- **Resource Group**: `rg-anela-heblo-{environment}`
- **App Service Plan**: Basic B1 (Test) / Standard S1 (Production)
- **Container Registry**: Docker Hub (public registry)
- **Database**: Azure Database for PostgreSQL (shared or separate per environment)

### Configuration:
- **Application Settings**: Environment variables injected via Azure Portal/ARM templates
- **Continuous Deployment**: Webhook from Docker Hub or GitHub Actions push
- **SSL/TLS**: Managed certificate via Azure (free for *.azurewebsites.net)
- **Custom Domain**: Optional for production (requires paid SSL certificate)

### Environment-Specific Settings:

**Test Environment:**
```json
{
  "ASPNETCORE_ENVIRONMENT": "Test",
  "ConnectionStrings__DefaultConnection": "postgresql://...",
  "REACT_APP_USE_MOCK_AUTH": "true"
}
```

**Production Environment:**
```json
{
  "ASPNETCORE_ENVIRONMENT": "Production",
  "ConnectionStrings__DefaultConnection": "postgresql://...",
  "AzureAd__ClientId": "xxx",
  "AzureAd__TenantId": "xxx",
  "REACT_APP_USE_MOCK_AUTH": "false"
}
```

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

## 9. 🧪 Test Organization Structure

**Frontend tests are organized by purpose and scope:**

- **UI/Layout Tests**: `/frontend/test/ui/layout/{component}/`
  - `sidebar/` - Sidebar collapse/expand, navigation, responsive behavior
  - `statusbar/` - Status bar positioning, content, responsiveness  
  - `auth/` - Authentication flows, login/logout UI behavior
  - `topbar/` - Top navigation, menu interactions
  - `general/` - Overall layout, responsive design, page structure
- **Component Tests**: `/frontend/test/components/` - Individual React component testing
- **Integration Tests**: `/frontend/test/integration/` - Component interaction testing
- **E2E Tests**: `/frontend/test/e2e/` - Full user journey testing

**Test Environment**: All Playwright tests MUST use automation environment (ports 3001/5001) with mock authentication.

---

## 10. 🔧 OpenAPI Client Generation

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

## 10. 🧠 AI Review Agent

- Since the project is developed by a single developer:
  - PR review is handled by an **AI agent**.
  - PRs must include CI success and optionally auto-reviewed comments.

---

## 11. 🔐 GitHub Secrets Configuration

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
Configured automatically during deployment:

**Test Environment:**
- `ASPNETCORE_ENVIRONMENT=Test`
- `REACT_APP_API_URL=https://anela-heblo-test.azurewebsites.net`
- `REACT_APP_USE_MOCK_AUTH=true`
- `WEBSITES_PORT=8080`

**Production Environment:**
- `ASPNETCORE_ENVIRONMENT=Production`
- `REACT_APP_API_URL=https://anela-heblo.azurewebsites.net`
- `REACT_APP_USE_MOCK_AUTH=false`
- `REACT_APP_AZURE_CLIENT_ID` (from secret)
- `REACT_APP_AZURE_AUTHORITY` (from secret)
- `WEBSITES_PORT=8080`

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

