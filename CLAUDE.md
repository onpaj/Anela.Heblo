# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a documentation repository for "Anela Heblo" - a cosmetics company workspace application. The repository contains comprehensive architecture documentation and design specifications for a full-stack web application that will be built later.

## Architecture Summary

**Stack**: Monorepo (.NET 8 + React), Single Docker image deployment, Azure Web App for Containers
- **Frontend**: React PWA with i18next localization, MSAL/Mock authentication, hot reload in dev
- **Backend**: ASP.NET Core (.NET 8) REST API with Hangfire background jobs, serves React static files
- **Database**: PostgreSQL with EF Core migrations
- **Authentication**: MS Entra ID (production) / Mock Auth (development/test)
- **Integrations**: ABRA Flexi (custom API client), Shoptet (Playwright-based scraping)
- **Testing**: Playwright for both E2E testing and Shoptet integration automation
- **Deployment**: Single Docker container to Azure Web App for Containers, GitHub Actions CI/CD

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
- `dotnet build` - Build the solution
- `dotnet test` - Run unit and integration tests (includes mock authentication tests)
- `dotnet ef migrations add <name>` - Create new migration
- `dotnet ef database update` - Apply migrations
- `dotnet run` - Start development server with mock authentication (if UseMockAuth=true)
- `dotnet msbuild -t:GenerateFrontendClientManual` - Manually generate TypeScript client for frontend

**Frontend (Standalone React)**:
- `npm install` - Install dependencies
- `npm start` - Start development server with hot reload (localhost:3000)
- `npm test` - Run tests with Jest/React Testing Library
- `npm run build` - Build static files for production deployment
- `npm run lint` - Run linter
- `npx playwright test` - Run end-to-end tests (when configured)
- `npx playwright codegen localhost:3000` - Generate test code by recording interactions

**Authentication Setup**:

**For Local Development (Mock Mode)**:
1. Set `"UseMockAuth": true` in `backend/src/Anela.Heblo.API/appsettings.Development.json`
2. Mock authentication automatically provides a "Mock User" with standard claims
3. No real Microsoft Entra ID credentials needed for development
4. API accepts all requests as authenticated with mock user data

**For Production/Real Authentication**:
1. Set `"UseMockAuth": false` in configuration
2. Copy `frontend/.env.example` to `frontend/.env`
3. Fill in actual Microsoft Entra ID credentials (client ID, tenant ID)
4. The `.env` file is gitignored and contains sensitive data - never commit it
5. Contact project owner for actual credential values

**Docker**:
- `docker-compose up` - Start local development environment (if needed)
- `docker build -t anela-heblo .` - Build single production image (multi-stage: frontend build + backend + static files)
- **Development**: Separate React dev server (hot reload) + ASP.NET Core API server
- **Production**: Single container serves both React static files and ASP.NET Core API

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

## Testing Strategy

**Backend Testing**:
- Integration tests using `Microsoft.AspNetCore.Mvc.Testing`
- Mock authentication for development testing (`UseMockAuth: true`)
- Test project: `backend/test/Anela.Heblo.Tests/`
- Run with: `dotnet test`

**Mock Authentication Testing**:
- `MockAuthenticationHandler` provides fake authenticated user
- Claims include: user ID, name, email, Entra ID specific claims (oid, tid, roles)
- Integration tests verify API endpoints work without real authentication
- Automatic test server configuration with mock auth enabled

**Frontend Testing**:
- Playwright for E2E testing and UI validation
- Port 3000 reserved for development and Playwright testing
- Component testing with React Testing Library
- Every time youre about to run playwright tests on local machine, do exactly:
  - kill all processes on port 3001 and 5001
  - run server and frontent in background. Dont wait for them, wait just 5 sec and continue with nex step
  - run ui tests (30 sec timeout each)
  - wait for tests to complete
  - kill server and frontend process

## Environment Configuration

- Uses `.env` files for shared configuration
- Frontend variables prefixed with `REACT_APP_`
- Backend uses `appsettings.{Environment}.json` + environment variables
- Database migrations applied manually (not automated in CI/CD)
- **Mock Authentication**: Set `"UseMockAuth": true` in `appsettings.Development.json` for local development

## Port Configuration

| Environment | Frontend Port | Backend Port | Container Port | Azure URL | Deployment Type |
|-------------|---------------|--------------|----------------|-----------|-----------------|
| **Local Development** | 3000 | 5000 | - | - | Separate servers (hot reload) |
| **Local Automation/Playwright** | 3001 | 5001 | - | - | Separate servers (testing) |
| **Test Environment** | 8080 | 5000 | 8080 | https://heblo-test.azurewebsites.net | Single container |
| **Production** | 8080 | 5000 | 8080 | https://heblo.anela.cz | Single container |

## Deployment Strategy

- **Development/Debug**: 
  - **Frontend**: Standalone React dev server (`npm start`) with **hot reload** (localhost:3000)
  - **Backend**: ASP.NET Core dev server (`dotnet run`) (localhost:5000)
  - **Architecture**: **Separate servers** to preserve hot reload functionality for development
  - **CORS**: Configured to allow frontend-backend communication
  - **Authentication**: Mock authentication enabled for both frontend and backend
  - **Why separate**: Hot reload is critical for development - single container would disable this feature
- **Test Environment**:
  - **Single Docker container** on Azure Web App for Containers
  - Container serves both React static files and ASP.NET Core API
  - Mock authentication enabled
  - URL: https://heblo-test.azurewebsites.net
- **Production Environment**:
  - **Single Docker container** on Azure Web App for Containers
  - Container serves both React static files and ASP.NET Core API
  - Real Microsoft Entra ID authentication
  - URL: https://heblo.anela.cz
- **Versioning**: Semantic versioning with conventional commits
- **CI/CD**: GitHub Actions with unit tests, Playwright UI tests, Docker builds, feature branch testing, main branch auto-deploy

## Design Document Alignment Rules

**MANDATORY**: All implementation work MUST align with the application design documents in `/docs`. Before making ANY changes to code, architecture, or design, Claude Code MUST:

### 1. Consultation Requirements

**Before ANY implementation work:**
- Read and understand relevant design documents from `/docs`
- Verify proposed changes align with documented architecture
- If conflicts arise, ask for clarification rather than making assumptions

**Required documents for different change types:**

- **Backend/API changes**: Consult `docs/📘 Architecture Documentation – MVP Work.md` for module definitions and data flow
- **Infrastructure/deployment changes**: Consult `docs/architecture/application_infrastructure.md` for deployment strategy and CI/CD rules  
- **Environment/configuration changes**: Consult `docs/architecture/environments.md` for port mappings, CORS, and Azure settings
- **Filesystem/structure changes**: Consult `docs/architecture/filesystem.md` for directory organization and file locations
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
- `docs/architecture/application_infrastructure.md` - Infrastructure, deployment, and CI/CD details  
- `docs/architecture/environments.md` - Environment configurations, ports, and Azure settings
- `docs/architecture/filesystem.md` - Directory structure and file organization
- `docs/ui_design_document.md` - UI/UX specifications and design system
- `CLAUDE.md` - This file for future Claude Code instances

This ensures documentation stays synchronized with actual implementation and architectural decisions.

### 4. Enforcement

- **NO implementation without consultation** - All code changes must reference appropriate design documents
- **NO architectural deviations without approval** - Stay within documented patterns unless explicitly asked to change them
- **Documentation-first approach** - When in doubt, follow the documentation; ask for updates if needed
- **MANDATORY documentation updates** - Whenever any application code, configuration, or implementation changes that is covered by documentation in `/docs`, the corresponding documentation MUST be updated immediately to reflect the changes

## Frontend Development & Testing Rules

### Playwright Integration with Automation Environment

**MANDATORY**: When running Playwright tests, Claude Code MUST ALWAYS use the "automation" environment:

**CI/CD Integration**: Playwright tests are automatically executed in the GitHub Actions CI pipeline for all pull requests, ensuring UI functionality is validated before merge.

1. **Automation Environment Configuration**:
   - **Backend**: Port 5001 with `ASPNETCORE_ENVIRONMENT=Automation`
   - **Frontend**: Port 3001 with automation-specific configuration
   - **Mock Authentication**: ALWAYS enabled in automation environment (never validate real Azure AD)
   - **Configuration Files**: Use `appsettings.Automation.json` for backend settings
   - **CRITICAL**: Backend Program.cs must recognize "Automation" environment and use mock auth handlers

2. **Required Startup Process**:
   - **Backend**: `cd backend/src/Anela.Heblo.API && ASPNETCORE_ENVIRONMENT=Automation dotnet run --launch-profile Automation`
   - **Frontend**: `cd frontend && npm run start:automation`
   - **Test URL**: Always use `http://localhost:3001` for Playwright tests
   - **API URL**: Automation frontend connects to `http://localhost:5001`

3. **Automation Scripts Available**:
   - `./scripts/start-automation-backend.sh` - Start backend on port 5001
   - `./scripts/start-automation-frontend.sh` - Start frontend on port 3001  
   - `./scripts/run-playwright-tests.sh` - Complete test runner with environment setup

4. **Port Isolation**:
   - **Development**: Frontend 3000 → Backend 5000
   - **Automation/Testing**: Frontend 3001 → Backend 5001
   - **Test Environment**: Single container on port 8080
   - **Production**: Single container on port 8080

5. **Visual Testing & Validation**:
   - Use `npx playwright codegen localhost:3001` to record user interactions
   - Port 3001 is reserved for Playwright testing in automation environment
   - Verify UI changes work correctly across different screen sizes
   - Test responsive behavior (mobile, tablet, desktop breakpoints)
   - Validate component states (hover, active, disabled, etc.)

6. **Interactive Development**:
   - Test complex user flows (sidebar collapse/expand, form submissions, navigation)
   - Verify accessibility features (keyboard navigation, screen reader compatibility)
   - Validate cross-browser compatibility when needed

7. **Regression Testing**:
   - After significant UI changes, run tests to ensure existing functionality still works
   - Test component interactions and state management
   - Verify responsive design adaptations

8. **When to Use Playwright**:
   - **Required**: Major layout changes, new component implementations
   - **Required**: Responsive design updates or sidebar behavior changes  
   - **Required**: Form interactions, navigation flows, or state-dependent UI
   - **Optional**: Minor styling tweaks or text changes

9. **Playwright Commands (Automation Environment)**:
   - `npx playwright install` - Install browsers (run once)
   - `npx playwright codegen localhost:3001` - Record interactions for testing
   - `./scripts/run-playwright-tests.sh` - Run complete test suite with environment setup
   - `npx playwright test --headed` - Run tests with visible browser
   - `npx playwright show-report` - View test results

10. **VS Code Launch Configurations**:
    - **"Launch Automation Environment"** - Starts both backend (port 5001) and frontend (port 3001) for testing
    - **"Run All UI Tests (Playwright)"** - Executes all Playwright tests (requires automation environment)
    - **"Run UI Tests (Headed)"** - Runs tests with visible browser for debugging
    - **"Run UI Tests (Debug)"** - Runs tests in debug mode with Playwright inspector
    
    **Usage**: First run "Launch Automation Environment", then run any of the test configurations.

11. **Test Organization Structure**:
    - **UI/Layout Tests**: `/frontend/test/ui/layout/{component}/`
      - `sidebar/` - Sidebar collapse/expand, navigation, responsive behavior
      - `statusbar/` - Status bar positioning, content, responsiveness
      - `auth/` - Authentication flows, login/logout UI behavior
      - `topbar/` - Top navigation, menu interactions
      - `general/` - Overall layout, responsive design, page structure
    - **Component Tests**: `/frontend/test/components/`
    - **Integration Tests**: `/frontend/test/integration/`
    - **E2E Tests**: `/frontend/test/e2e/`

12. **CRITICAL: Background Process Management**:
    - **ALWAYS run servers in background**: Use `&` at end of commands
    - **Backend**: `cd backend/src/Anela.Heblo.API && ASPNETCORE_ENVIRONMENT=Automation dotnet run --launch-profile Automation &`
    - **Frontend**: `cd frontend && npm run start:automation &`
    - **Store PIDs**: Capture process IDs for cleanup: `BACKEND_PID=$!` and `FRONTEND_PID=$!`
    - **MANDATORY Cleanup**: After tests, kill background processes: `kill $BACKEND_PID $FRONTEND_PID 2>/dev/null || true`
    - **Port cleanup**: Also kill by port: `lsof -ti:3001 | xargs kill -9 2>/dev/null || true`
    - **Never wait for servers to finish**: They run indefinitely, use background execution
    - **CRITICAL: Never wait after cleanup**: Command must end immediately after killing processes, no waiting!
    - **PROBLEM**: If tests hang or wait after completion, interrupt immediately
    - **SOLUTION**: Use `timeout` or ensure command structure ends properly: `command && cleanup || true`

### Development Workflow

1. **Make UI changes** to React components
2. **Start dev server** with `npm start` (development - port 3000)
3. **For Playwright testing**: Always use automation environment:
   - Start backend: `cd backend/src/Anela.Heblo.API && ASPNETCORE_ENVIRONMENT=Automation dotnet run --launch-profile Automation`
   - Start frontend: `cd frontend && npm run start:automation`
   - Run tests: `./scripts/run-playwright-tests.sh` or `npx playwright test`
4. **Use Playwright** to record and validate interactions on `localhost:3001`
5. **Test responsive behavior** across breakpoints
6. **Verify accessibility** and keyboard navigation
7. **Run build** to ensure no compilation errors

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

## Code Quality & Formatting Rules

### .NET Backend Code Standards

**MANDATORY**: All C# code MUST follow .NET formatting standards and pass `dotnet format` validation:

- **Indentation**: 4 spaces for C# code (never tabs)
- **Line endings**: Use proper line breaks and spacing
- **Brace style**: Allman style (opening braces on new line)
- **Method spacing**: Proper spacing between methods and classes
- **Using statements**: Organized and deduplicated
- **Null checks**: Use modern null-conditional operators where appropriate

**Automatic formatting enforcement:**
- Before any commit, run `dotnet format` to fix formatting issues
- CI/CD pipeline validates code formatting and fails on violations
- All generated code must pass formatting validation

**Code generation requirements:**
- Generate code that inherently follows .NET formatting standards
- Use consistent indentation and spacing from the start
- Follow established patterns in existing codebase
- Never generate code that requires post-generation formatting fixes

### Frontend Code Standards

**React/TypeScript formatting:**
- Use Prettier for consistent formatting
- 2 spaces indentation for TypeScript/JSX
- Semicolons required
- Single quotes for strings
- Trailing commas in objects/arrays

**Enforcement:**
- Run `npm run lint` before commits
- ESLint and Prettier configurations must be respected
- Generated components follow existing code patterns

## Important Notes

- This is a **solo developer project** with AI-assisted PR reviews
- Database migrations are **manual** - not part of automated deployment  
- EF Core is used for database access and migrations
- OpenAPI client generation for frontend (post-build step)
- All Docker images pushed to Docker Hub
- Observability via Application Insights