# 📁 Filesystem Structure Documentation

This document defines the project's directory structure and filesystem organization.

---

## 📁 Directory Structure

```
/                  # Monorepo root
├── backend/       # Backend – ASP.NET Core application
│   ├── src/       # Application code
│   │   ├── Anela.Heblo.API/           # Main API project (serves React app)
│   │   │   ├── Controllers/           # API controllers
│   │   │   ├── Extensions/            # Service registration & configuration extensions
│   │   │   │   ├── ServiceCollectionExtensions.cs
│   │   │   │   ├── LoggingExtensions.cs
│   │   │   │   ├── ApplicationBuilderExtensions.cs
│   │   │   │   └── AuthenticationExtensions.cs
│   │   │   ├── Constants/             # Configuration constants
│   │   │   │   └── ConfigurationConstants.cs
│   │   │   ├── Authentication/        # Authentication handlers
│   │   │   │   └── MockAuthenticationHandler.cs
│   │   │   └── Program.cs             # Application entry point (simplified)
│   │   ├── Anela.Heblo.API.Client/    # Auto-generated OpenAPI client
│   │   ├── Anela.Heblo.Application/   # Application layer (Clean Architecture)
│   │   │   ├── Interfaces/            # Service interfaces
│   │   │   │   ├── IWeatherService.cs
│   │   │   │   ├── IUserService.cs
│   │   │   │   └── ITelemetryService.cs
│   │   │   └── Services/              # Application service implementations
│   │   │       ├── WeatherService.cs
│   │   │       └── UserService.cs
│   │   ├── Anela.Heblo.Domain/        # Domain layer (Clean Architecture)
│   │   │   ├── Entities/              # Domain entities
│   │   │   │   └── WeatherForecast.cs
│   │   │   └── Constants/             # Domain constants
│   │   │       └── WeatherConstants.cs
│   │   └── Anela.Heblo.Infrastructure/ # Infrastructure layer (Clean Architecture)
│   │       └── Services/              # Infrastructure service implementations
│   │           └── TelemetryService.cs (with NoOpTelemetryService)
│   ├── test/      # Unit/integration tests for backend
│   │   └── Anela.Heblo.Tests/         # Integration tests
│   ├── migrations/ # EF Core database migrations
│   └── scripts/   # Utility scripts (e.g. DB tools, backups)
│
├── frontend/      # React PWA (builds into backend wwwroot)
│   ├── public/     # Static assets (index.html, favicon, etc.)
│   ├── src/
│   │   ├── components/
│   │   │   └── __tests__/    # Component unit tests
│   │   ├── pages/
│   │   │   └── __tests__/    # Page component tests
│   │   ├── api/         # API client and services
│   │   │   └── __tests__/    # API client unit tests
│   │   ├── auth/        # Authentication logic
│   │   │   └── __tests__/    # Authentication tests
│   │   ├── config/      # Configuration management
│   │   │   └── __tests__/    # Configuration tests
│   │   └── ...
│   ├── test/       # UI automation tests (Playwright only)
│   │   ├── ui/          # UI/Layout tests (Playwright)
│   │   │   └── layout/  # Layout component UI tests
│   │   ├── integration/ # Integration tests
│   │   └── e2e/         # End-to-end tests
│   └── package.json # Node.js dependencies and scripts
│
├── docs/          # Project documentation
│   ├── architecture/       # Architecture documentation
│   │   ├── filesystem.md
│   │   ├── environments.md
│   │   ├── application_infrastructure.md
│   │   └── observability.md
│   ├── design/            # UI/UX design documentation
│   │   ├── ui_design_document.md
│   │   ├── layout_definition.md
│   │   └── styleguide.md
│   ├── features/          # Feature-specific documentation
│   │   └── Authentication.md
│   └── tasks/             # Reusable task definitions
│       ├── backend-clean-architecture-refactoring.md
│       └── AUTHENTICATION_TESTING.md
├── scripts/       # Development and deployment scripts
│   ├── build-and-push.sh
│   ├── deploy-azure.sh
│   └── run-playwright-tests.sh
├── .github/        # GitHub Actions workflows
├── .env            # Dev environment variables
├── Dockerfile      # Single image for backend + frontend
├── docker-compose.yml # For local dev/test if needed
├── CLAUDE.md       # AI assistant instructions
└── .dockerignore   # Docker build optimization
```
## 🏗️ Clean Architecture Implementation

**The backend follows Clean Architecture principles with proper layer separation:**

- **Domain Layer** (`Anela.Heblo.Domain`): Core business entities and constants
- **Application Layer** (`Anela.Heblo.Application`): Business logic and service interfaces
- **Infrastructure Layer** (`Anela.Heblo.Infrastructure`): External service implementations
- **API Layer** (`Anela.Heblo.API`): HTTP controllers and application configuration
- **Files should be kept in layers together by features (vertical slices), not by type (horizontal slices)**
  - **Example**: `backend/src/Anela.Heblo.Application/UserManagement including both interface and service implementation`

**Dependency Flow**: API → Application/Infrastructure → Domain

**Key Features:**
- Dependency injection with proper service lifetime management
- SOLID principles adherence
- Professional logging and configuration management
- Modular service registration through extension methods
- Clean separation of concerns

---

## 🧪 Test Organization Structure

**Frontend tests follow standard React patterns:**

### **Unit & Integration Tests (Jest + React Testing Library)**
**Tests are located in `__tests__/` folders next to the components they test:**

- **`/frontend/src/api/__tests__/`** - API client unit tests
  - `api-client.test.ts` - Bearer token authentication, error handling
  - `client.test.ts` - Client factory and configuration tests
- **`/frontend/src/components/__tests__/`** - React component tests
  - Individual component test files (e.g., `Button.test.tsx`)
- **`/frontend/src/components/pages/__tests__/`** - Page component tests
  - `WeatherTest.test.tsx` - Page component integration tests
- **`/frontend/src/auth/__tests__/`** - Authentication logic tests
  - `useAuth.test.ts` - Real Azure AD authentication hook tests
  - `mockAuth.test.ts` - Mock authentication tests
- **`/frontend/src/config/__tests__/`** - Configuration management tests
  - `runtimeConfig.test.ts` - Runtime configuration loading tests

### **UI Automation Tests (Playwright)**
**UI tests are in separate `/frontend/test/` directory:**

- **`/frontend/test/ui/layout/{component}/`** - Visual and interaction tests
  - `sidebar/` - Sidebar collapse/expand, navigation, responsive behavior
  - `statusbar/` - Status bar positioning, content, responsiveness  
  - `auth/` - Authentication flows, login/logout UI behavior
  - `topbar/` - Top navigation, menu interactions
  - `general/` - Overall layout, responsive design, page structure
- **`/frontend/test/integration/`** - Component interaction testing
- **`/frontend/test/e2e/`** - Full user journey testing

**CRITICAL Test Environment Rules:**
- **Unit/Integration Tests**: Use Jest with mocked dependencies, located in `__tests__/` folders
- **UI/Playwright Tests**: MUST use automation environment (ports 3001/5001) with mock authentication, located in `/frontend/test/`
- **Test Co-location**: Unit tests are co-located with components for easy maintenance

---

## 🔧 OpenAPI Client Generation

### Backend C# Client

- **Location**: `backend/src/Anela.Heblo.API.Client/`
- **Auto-generation**: PostBuild event in API project (Debug mode only)
- **Tool**: NSwag with System.Text.Json
- **Output**: `Generated/AnelaHebloApiClient.cs`
- **Manual Generation**: Scripts available (`generate-client.ps1`, `generate-client.sh`)

  

### Frontend TypeScript Client

- **Location**: `frontend/src/api/generated/api-client.ts`
- **Auto-generation**: Via backend PostBuild event or frontend prebuild script
- **Tool**: NSwag with Fetch API template (currently placeholder implementation with bearer token support)
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
- **Frontend Client**: `frontend/src/api/generated/`