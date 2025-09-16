# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is an implementation repository for "Anela Heblo" - a cosmetics company workspace application. The project is actively being developed following Clean Architecture principles with a full-stack .NET 8 + React architecture.

## Architecture Summary

**Stack**: Monorepo (.NET 8 + React), **Clean Architecture** with Vertical Slice organization, MediatR + **MVC Controllers**, Single Docker image deployment, Azure Web App for Containers

**IMPORTANT ARCHITECTURAL UPDATE**: The backend has evolved from a simple Vertical Slice implementation to a proper Clean Architecture with clear layer separation:
- **Domain Layer** (`Anela.Heblo.Domain`): Contains domain entities, repository interfaces, domain services
- **Application Layer** (`Anela.Heblo.Application`): MediatR handlers, business logic, DTOs
- **Infrastructure Layer** (`Anela.Heblo.Persistence`): Database contexts, EF configurations, repository implementations
- **API Layer** (`Anela.Heblo.API`): MVC Controllers (not FastEndpoints), authentication, serves React app
- **Frontend**: React PWA with i18next localization, MSAL/Mock authentication, hot reload in dev
- **Backend**: ASP.NET Core (.NET 8) with Clean Architecture, MediatR pattern, Hangfire background jobs, serves React static files
    - **Anela.Heblo.API**: Host/Composition layer with MVC Controllers (NOT FastEndpoints)
  - **Anela.Heblo.Domain**: Domain layer with entities, repository interfaces, domain services
  - **Anela.Heblo.Application**: Application layer with MediatR handlers, DTOs, business logic
  - **Anela.Heblo.Persistence**: Infrastructure layer with EF Core contexts, entity configurations, repository implementations
- **Database**: PostgreSQL with EF Core migrations
- **Authentication**: MS Entra ID (production) / Mock Auth (development/test)
- **Integrations**: ABRA Flexi (custom API client), Shoptet (Playwright-based scraping)
- **Testing**: Playwright for both E2E testing and Shoptet integration automation
- **Deployment**: Single Docker container to Azure Web App for Containers, GitHub Actions CI/CD

## Repository Structure (Clean Architecture with Vertical Slice Organization)

**Current Clean Architecture Implementation with MediatR + Controllers:**
```
/                  # Monorepo root
├── backend/       # Backend – ASP.NET Core application
│   ├── src/       # Application code
│   │   ├── Anela.Heblo.API/           # Host/Composition layer
│   │   │   ├── Controllers/           # MVC Controllers for API endpoints
│   │   │   │   └── {Feature}Controller.cs # One controller per feature
│   │   │   ├── Extensions/            # Service registration & configuration
│   │   │   ├── Authentication/        # Authentication handlers
│   │   │   └── Program.cs             # Application entry point
│   │   ├── Anela.Heblo.Domain/        # Domain layer
│   │   │   ├── Features/              # Feature-specific domain objects
│   │   │   │   └── {Feature}/         # Feature domain folder
│   │   │   │       ├── {Entity}.cs    # Domain entities
│   │   │   │       ├── I{Entity}Repository.cs # Repository interfaces
│   │   │   │       └── {Subdomain}/   # Optional subdomains for complex features
│   │   │   └── Shared/                # Cross-cutting domain utilities
│   │   ├── Anela.Heblo.Application/   # Application layer
│   │   │   ├── Features/              # Feature-specific application services
│   │   │   │   └── {Feature}/         # Feature application folder
│   │   │   │       ├── UseCases/      # MediatR handlers (for complex features)
│   │   │   │       │   └── {UseCase}/ # Use case folder: Handler.cs, Request.cs, Response.cs
│   │   │   │       ├── Contracts/     # Shared DTOs across use cases
│   │   │   │       ├── Services/      # Domain services and business logic
│   │   │   │       ├── Infrastructure/ # Feature infrastructure
│   │   │   │       ├── Validators/    # FluentValidation request validators
│   │   │   │       ├── {Feature}Repository.cs # Repository implementation
│   │   │   │       ├── {Feature}MappingProfile.cs # AutoMapper profile
│   │   │   │       ├── {Feature}Constants.cs # Feature constants
│   │   │   │       └── {Feature}Module.cs # DI registration
│   │   │   └── ApplicationModule.cs   # Central module registration
│   │   ├── Anela.Heblo.Persistence/   # Infrastructure layer
│   │   │   ├── ApplicationDbContext.cs # Single DbContext (initially)
│   │   │   ├── {Feature}/             # Feature-specific persistence (complex features)
│   │   │   │   ├── {Entity}Configuration.cs # EF Core entity configurations
│   │   │   │   └── {Entity}Repository.cs    # Feature-specific repositories
│   │   │   ├── Repositories/          # Generic/shared repositories
│   │   │   ├── Migrations/            # EF Core migrations
│   │   │   └── PersistenceModule.cs   # DI registration
│   │   └── Anela.Heblo.API.Client/    # Auto-generated OpenAPI client
│   ├── test/      # Unit/integration tests
│   └── scripts/   # Utility scripts
│
├── frontend/      # React PWA (builds into backend wwwroot)
│   ├── public/     # Static assets (index.html, favicon, etc.)
│   ├── src/
│   │   ├── components/    # React components with co-located __tests__/
│   │   ├── pages/         # Page components with co-located __tests__/
│   │   ├── api/           # API client and services with co-located __tests__/
│   │   └── [other areas]  # Other frontend areas with co-located __tests__/
│   ├── test/       # UI automation tests (Playwright)
│   │   └── e2e/         # End-to-end tests
│   └── package.json # Node.js dependencies and scripts
│
├── docs/          # Project documentation
├── scripts/       # Development and deployment scripts
├── .github/       # GitHub Actions workflows
└── [configuration files]
```

**🏗️ Clean Architecture with Vertical Slice Organization:**
- **Clean Architecture layers**: API (Host), Domain, Application, Persistence (Infrastructure)
- **MediatR Pattern**: Controllers send requests to handlers via MediatR for clean separation
- **Vertical Organization**: Features organized vertically but respecting Clean Architecture boundaries
- **Standard API Pattern**: All endpoints follow /api/{controller} REST conventions
- **Feature Autonomy**: Each feature manages its own contracts, services, and infrastructure
- **SOLID Principles**: Applied within each vertical slice and across layers
- **Testability**: Each handler can be tested in isolation
- **Maintainability**: Changes to a feature are localized to its module

## Feature Organization Patterns

### All Features:
```
Features/{Feature}/
├── UseCases/                   # Use case handlers organized by functionality
│   ├── Get{Entity}List/
│   │   ├── Get{Entity}ListHandler.cs
│   │   ├── Get{Entity}ListRequest.cs
│   │   └── Get{Entity}ListResponse.cs
│   ├── Get{Entity}Detail/
│   ├── Create{Entity}/
│   ├── Update{Entity}/
│   └── Delete{Entity}/
├── Contracts/                  # Shared DTOs across use cases
│   ├── {Entity}Dto.cs
│   └── [Other shared DTOs]
├── Services/                   # Domain services and business logic
│   ├── I{Entity}Service.cs
│   └── {Entity}Service.cs
├── Infrastructure/             # Feature infrastructure
│   ├── {Entity}Scheduler.cs
│   ├── {Entity}FeatureFlags.cs
│   └── Exceptions/
├── Validators/                 # Request validation
│   ├── Create{Entity}RequestValidator.cs
│   └── Update{Entity}RequestValidator.cs
├── {Feature}Repository.cs      # Feature repository
├── {Feature}MappingProfile.cs  # AutoMapper profile
├── {Feature}Constants.cs       # Feature constants
└── {Feature}Module.cs         # DI registration
```

## Component Placement Rules

### API Layer (`Anela.Heblo.API`):
- **Controllers/**: MVC Controllers that expose REST endpoints
  - One controller per feature: `{Feature}Controller.cs`
  - Controllers only orchestrate MediatR requests
  - Follow `/api/{controller}` routing pattern

### Domain Layer (`Anela.Heblo.Domain`):
- **Features/{Feature}/**: Domain entities, aggregates, repository interfaces
  - Domain entities: `{Entity}.cs`
  - Repository contracts: `I{Entity}Repository.cs`
  - Domain services interfaces
  - For complex domains, use subfolders: `{Feature}/{Subdomain}/`

### Application Layer (`Anela.Heblo.Application`):
- **Features/{Feature}/UseCases/**: MediatR handlers (business operations)
  - Each use case in separate folder with Handler, Request, Response
  - Use case naming: `Get{Entity}List`, `Create{Entity}`, `Update{Entity}`
- **Features/{Feature}/Contracts/**: Shared DTOs across multiple use cases
- **Features/{Feature}/Services/**: Domain services, background services
- **Features/{Feature}/Infrastructure/**: Feature-specific infrastructure
- **Features/{Feature}/Validators/**: FluentValidation request validators
- **Features/{Feature}/{Feature}Repository.cs**: Repository implementations
- **Features/{Feature}/{Feature}Module.cs**: DI container registration

### Infrastructure Layer (`Anela.Heblo.Persistence`):
- **ApplicationDbContext.cs**: Single DbContext (initially)
- **{Feature}/{Subdomain}/**: Feature-specific persistence (complex features)
  - Entity configurations: `{Entity}Configuration.cs`
  - Repository implementations: `{Entity}Repository.cs`
- **Repositories/**: Generic/shared repositories (`BaseRepository.cs`)
- **Mapping/**: Database-specific mappers for external systems
- **Migrations/**: EF Core migrations
- **PersistenceModule.cs**: DI container registration

## Key Principles

- **Vertical organization**: Each feature contains all its layers
- **MediatR pattern**: Controllers send requests to handlers via MediatR
- **Handlers as Application Services**: Business logic resides in MediatR handlers
- **Standard endpoints**: All endpoints follow `/api/{controller}` pattern
- **Feature autonomy**: Each feature manages its own contracts, services, and infrastructure
- **SOLID principles**: Applied within each vertical slice

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
- **Layout**: Full-height sidebar navigation + main content area (no topbar)
- **Sidebar**: 
  - **Structure**: App title at top, navigation items in middle, user info + toggle at bottom
  - **Expanded**: `w-64` (256px) with full navigation and text labels
  - **Collapsed**: `w-16` (64px) with app logo, icons only, and tooltips
  - **Toggle**: Via button in sidebar bottom (next to user info)
  - **Button location**: Bottom-right when expanded, bottom-center when collapsed
  - **Icons**: `PanelLeftClose` (collapse) / `PanelLeftOpen` (expand)
  - **Animation**: Smooth `transition-all duration-300` for width changes
  - **Content adaptation**: Main content adapts with `md:pl-64` or `md:pl-16`
- **App Title**: "Anela Heblo" displayed at top of sidebar
- **User Profile**: Located in sidebar bottom with dropdown menu (login/logout)
- **Page Layout Standards**: **CRITICAL** - All pages must follow standardized container structure defined in `docs/design/layout_definition.md` (Page Layout Structure Rules)
- **Colors**: Gray-based palette with indigo accents, emerald success states
- **Icons**: Lucide React for consistent, modern iconography
- **Typography**: System fonts with defined hierarchy (XL headings to XS captions)
- **Components**: Consistent buttons, forms, tables with hover states
- **Responsiveness**: Mobile-first approach with sidebar overlay on mobile, fixed on desktop
- **Localization**: Czech language primary, i18next framework

## OpenAPI Client Generation

### Backend C# Client
- **Location**: `backend/src/Anela.Heblo.API.Client/`
- **Auto-generation**: PostBuild event in API project (Debug mode only)
- **Tool**: NSwag with System.Text.Json
- **Output**: `Generated/AnelaHebloApiClient.cs`

### Frontend TypeScript Client
- **Location**: `frontend/src/api/generated/api-client.ts`
- **Auto-generation**: Via backend PostBuild event or frontend prebuild script
- **Tool**: NSwag with Fetch API template
- **Build Integration**: Automatically generated before frontend build
- **Integration**: TanStack Query for data fetching and caching
- **Usage**: React hooks in `frontend/src/api/hooks.ts`
- **Endpoint Pattern**: All API endpoints follow `/api/{controller}` standard REST pattern

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

### API Endpoint Examples:
- GET `/api/weather/forecast` - Weather data
- GET `/api/configuration` - Application configuration
- Future: `/api/catalog`, `/api/invoices`, `/api/orders`, etc.

### CRITICAL: API Client URL Construction Rules

**MANDATORY**: All API hooks MUST use absolute URLs with baseUrl to avoid calling wrong endpoints.

**❌ WRONG - Relativní URL (volá frontend port místo backend)**:
```typescript
const url = `/api/catalog`; // Toto volá localhost:3001/api/catalog místo localhost:5001/api/catalog
const response = await (apiClient as any).http.fetch(url, {method: 'GET'});
```

**✅ CORRECT - Absolutní URL s baseUrl**:
```typescript
const relativeUrl = `/api/catalog`;
const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`; // http://localhost:5001/api/catalog
const response = await (apiClient as any).http.fetch(fullUrl, {method: 'GET'});
```

**Alternative správný pattern (jako v usePurchaseOrders.ts)**:
```typescript
// Vlastní API client třída s makeRequest metodou
async makeRequest<T>(url: string, options: RequestInit = {}): Promise<T> {
  const response = await fetch(`${this.baseUrl}${url}`, {...}); // Správně používá baseUrl
  return response.json();
}
```

**Enforcement Rules**:
- NIKDY nepoužívat relativní URLs přímo v fetch calls
- Pro všechny hooks používat generovaný API client prostřednictvím `getAuthenticatedApiClient()`


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
- Port 3000 reserved for development
- Component testing with React Testing Library
- To validate some frontend behavior, use playwright MCP server agains port 3000. Both frontend and backend should be running by default, ask user to run then if they are not running
- To run playwright tests, always user script ./scripts/run-playwright-tests.sh with optional parameter of test name (runs all UI tests when test name is not defined)

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

### Playwright Integration with Staging Environment

**MANDATORY**: Playwright tests MUST run against the deployed staging environment at https://heblo.stg.anela.cz

1. **Test Environment**:
   - **Target URL**: https://heblo.stg.anela.cz (deployed staging environment)
   - **Test Location**: `/frontend/test/e2e/` directory and subfolders
   - **Authentication**: Real Microsoft Entra ID authentication (not mock)
   - **Data**: Test staging data with real application state

2. **Visual Testing & Validation**:
   - Use `npx playwright codegen https://heblo.stg.anela.cz` to record user interactions
   - Test against deployed staging environment for realistic behavior
   - Verify UI changes work correctly across different screen sizes
   - Test responsive behavior (mobile, tablet, desktop breakpoints)
   - Validate component states (hover, active, disabled, etc.)

3. **Interactive Development**:
   - Test complex user flows (sidebar collapse/expand, form submissions, navigation)
   - Validate cross-browser compatibility when needed
   - Test real API integrations and data flows

4. **Regression Testing**:
   - After significant UI changes, run tests to ensure existing functionality still works
   - Test component interactions and state management against real backend
   - Verify responsive design adaptations with actual deployed styles

5. **When to Use Playwright**:
   - **Required**: Major layout changes, new component implementations
   - **Required**: Responsive design updates or sidebar behavior changes  
   - **Required**: Form interactions, navigation flows, or state-dependent UI
   - **Required**: Authentication flow changes or user management features
   - **Optional**: Minor styling tweaks or text changes

6. **Playwright Commands (Staging Environment)**:
   - `npx playwright install` - Install browsers (run once)
   - `npx playwright codegen https://heblo.stg.anela.cz` - Record interactions against staging
   - `./scripts/run-playwright-tests.sh` - Run complete E2E test suite against staging
   - `./scripts/run-playwright-tests.sh [test-name]` - Run specific test against staging
   - `npx playwright test --headed` - Run tests with visible browser against staging
   - `npx playwright show-report` - View test results

7. **Test Organization Structure**:
    
    ### **Unit & Integration Tests (Jest + React Testing Library)**
    **Tests are located in `__tests__/` folders next to the components they test:**
    - **`src/api/__tests__/`** - API client unit tests (mocked)
    - **`src/components/__tests__/`** - React component tests (isolated)
    - **`src/pages/__tests__/`** - Page component tests (mocked dependencies)
    - **`src/auth/__tests__/`** - Authentication logic tests (mocked)
    - **`src/config/__tests__/`** - Configuration management tests

    ### **E2E Tests (Playwright)**
    **E2E tests are in `/frontend/test/e2e/` directory and subfolders:**
    - **`test/e2e/auth/`** - Authentication flows with real Microsoft Entra ID
    - **`test/e2e/navigation/`** - Navigation flows and routing
    - **`test/e2e/layout/`** - Layout behavior (sidebar, responsive design)
    - **`test/e2e/features/`** - Feature-specific user journeys
    - **`test/e2e/integration/`** - Cross-component integration testing
    
    **CRITICAL Test Environment Rules:**
    - **Unit/Integration Tests**: Use Jest with mocked dependencies, co-located with components  
    - **E2E Tests**: MUST run against staging environment (https://heblo.stg.anela.cz), located in `/frontend/test/e2e/`

8. **Authentication Testing**:
   - Tests run against real Microsoft Entra ID on staging environment
   - No mock authentication - tests use actual login flows
   - Requires valid staging environment credentials
   - Tests verify complete authentication integration

### Development Workflow

1. **Make UI changes** to React components
2. **Start dev server** with `npm start` (development - port 3000) for local development
3. **For Playwright testing**: Always use deployed staging environment:
   - Target: https://heblo.stg.anela.cz
   - Run tests: `./scripts/run-playwright-tests.sh` or `npx playwright test`
   - No local server setup required for E2E testing
4. **Use Playwright** to record and validate interactions on staging environment
5. **Test responsive behavior** across breakpoints on staging
6. **Verify accessibility** and keyboard navigation on staging
7. **Run build** to ensure no compilation errors
8. **Stick with dev best practices (YAGNI, SOLID, KISS, DRY)**
9. **Keep code clean and readable** - follow established patterns
10. **Do not hesitate to ask for clarification** if unsure about implementation details or design decisions
11. **Do not hesitate to propose refactoring** if you see opportunities for improvement
12. Before finishing TODO items and stating that all tasks are completed, claude should validate both FE and BE builds for no compile errors, in case there were some changes (BE build for BE changes, FE build for FE changes)

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

### Enforcement
- All credential files are in `.gitignore`
- Tests fail if credentials are missing
- Code review must catch any hardcoded secrets

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

## Backend DTO and API Request/Response Design Rules

**CRITICAL: OpenAPI Client Generation Compatibility**

**Use Classes Instead of Records for API DTOs:**
- ❌ **NEVER use C# records for API request/response DTOs** - OpenAPI generators have issues with parameter order and property detection
- ✅ **ALWAYS use classes with properties for API DTOs** - ensures reliable OpenAPI schema generation
- ✅ **Add proper validation attributes** - `[Required]`, `[JsonPropertyName]`, etc.


**Enforcement:**
- All MediatR requests/responses for API endpoints must use classes
- Internal domain objects can still use records if not exposed via API
- API client regeneration automatically picks up new properties when using classes

## Implementation Guidelines

### When Creating New Features:
1. **Start with Domain**: Define entities and repository interfaces in `Domain/Features/{Feature}/`
2. **Add Application Logic**: Create handlers in `Application/Features/{Feature}/`
3. **Configure Persistence**: Create entity configurations in `Persistence/{Feature}/` and repository implementations
4. **Expose via API**: Create controller in `API/Controllers/{Feature}Controller.cs`
5. **Register Dependencies**: Update `{Feature}Module.cs` and `PersistenceModule.cs` for proper DI registration

### Naming Conventions:
- **Controllers**: `{Feature}Controller` (e.g., `CatalogController`)
- **Handlers**: `{Action}{Entity}Handler` (e.g., `GetCatalogListHandler`)
- **Requests/Responses**: `{Action}{Entity}Request/Response` (e.g., `GetCatalogListRequest`)
- **DTOs**: `{Entity}Dto` (e.g., `CatalogItemDto`)
- **Services**: `{Entity}Service` and `I{Entity}Service`
- **Entity Configurations**: `{Entity}Configuration` (e.g., `PurchaseOrderConfiguration`)
- **Repository Implementations**: `{Entity}Repository` (e.g., `TransportBoxRepository`)

### Evolution Path:
- **Shared → Feature-specific**: Move shared concerns into feature-specific implementations as needed
- **Single → Multiple DbContexts**: Eventually split database contexts per feature for better isolation

## Important Notes

- This is a **solo developer project** with AI-assisted PR reviews
- Database migrations are **manual** - not part of automated deployment  
- EF Core is used for database access and migrations
- OpenAPI client generation for frontend (post-build step)
- All Docker images pushed to Docker Hub
- Observability via Application Insights
- **Backend follows Clean Architecture with Vertical Slice organization** - see `/docs/architecture/filesystem.md` for detailed structure
- To run playwright tests always use ./scripts/run-playwright-tests.sh script, this script does not require any confirmation from user
- To debug single playwright tests, also use ./scripts/run-playwright-tests.sh using its parameter to run single test
- Use this script even when asked "create a playwright test for it to debug" something.. Create test and debug it using that script.
- Every time you would like to check if implementation is working, test for that instead of just launching the app (unit test, integration test or end to end ui test)
- There are 3 kinds of tests - BE (Backend, .net tests), FE (Frontend, Jest tests), UI (User interface tests, Playwright)

