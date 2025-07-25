# 📁 Filesystem Structure Documentation

This document defines the project's directory structure and filesystem organization.

---

## 📁 Directory Structure

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

## 🧪 Test Organization Structure

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

## 🔧 OpenAPI Client Generation

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

## 📦 Key File Locations

### Configuration Files
- **Environment Variables**: `.env` (project root)
- **Backend Settings**: `backend/src/Anela.Heblo.API/appsettings.{Environment}.json`
- **Frontend Settings**: `frontend/.env` (environment-specific)

### Build & Deployment
- **Docker**: `Dockerfile` (project root)
- **Compose**: `docker-compose.yml` (project root)
- **CI/CD**: `.github/workflows/` (GitHub Actions)

### Database
- **Migrations**: `backend/migrations/` (EF Core)
- **Scripts**: `backend/scripts/` (utility tools)

### Generated Code
- **Backend Client**: `backend/src/Anela.Heblo.API.Client/Generated/`
- **Frontend Client**: `frontend/src/services/generated/`