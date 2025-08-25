# Testing Strategy

This document defines the comprehensive testing approach for the Anela Heblo application, covering all testing layers from unit tests to end-to-end validation.

## Testing Philosophy & Test Pyramid

### Core Testing Principles

1. **Purpose-Driven Testing**: Every test must validate specific business logic, edge cases, or user scenarios - never just achieve coverage metrics
2. **Test Pyramid Structure**: 
   - **70% Unit Tests** - Fast, isolated business logic validation
   - **20% Integration Tests** - API endpoints, database interactions, service integrations
   - **10% E2E Tests** - Critical user journeys and complex workflows only
3. **Business Value Focus**: Test behavior and business rules, not implementation details
4. **Fail-Fast Principle**: Tests should catch issues as early as possible in the development cycle
5. **Maintainability Over Coverage**: Prefer maintainable tests with clear business value over high coverage percentages

### When to Use Each Test Type

| Test Type | Use When | Examples |
|-----------|----------|----------|
| **Unit Tests** | Testing business logic, calculations, validation rules, domain models | MediatR handlers, domain services, value objects |
| **Integration Tests** | Testing API endpoints, database operations, external service calls | Controller endpoints, repository operations, background jobs |
| **E2E Tests** | Testing critical user workflows, complex UI interactions | Authentication flow, order creation, responsive design |

## Backend Testing (.NET 8)

### Technology Stack

- **Test Framework**: xUnit for all .NET tests
- **Mocking**: Moq for dependency mocking
- **Test Data**: AutoFixture for generating test data (reduces boilerplate)
- **Assertions**: FluentAssertions for readable test assertions
- **Integration Testing**: Microsoft.AspNetCore.Mvc.Testing for API testing

### Unit Testing Standards

#### Test Structure Pattern
```csharp
public class CreatePurchaseOrderHandlerTests
{
    private readonly Mock<IRepository> _repositoryMock;
    private readonly Mock<IService> _serviceMock;
    private readonly Handler _handler;

    public CreatePurchaseOrderHandlerTests()
    {
        // Setup mocks and dependencies
        _repositoryMock = new Mock<IRepository>();
        _serviceMock = new Mock<IService>();
        _handler = new Handler(_repositoryMock.Object, _serviceMock.Object);
    }

    [Fact]
    public async Task Handle_WithValidInput_ShouldReturnExpectedResult()
    {
        // Arrange
        var request = CreateValidRequest();
        SetupMockBehaviors();

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Property.Should().Be(expectedValue);
        VerifyMockCalls();
    }
}
```

#### What to Unit Test (Required)
- **MediatR Handlers**: All business logic, validation, error scenarios
- **Domain Services**: Calculation logic, business rules, domain validations
- **Value Objects**: Equality, validation, business rules
- **Domain Entities**: Business behavior, state changes, invariants
- **Validators**: FluentValidation rules and edge cases
- **Extensions/Utilities**: Helper methods and transformations

#### What NOT to Unit Test
- Framework code (ASP.NET Core, Entity Framework)
- Simple getters/setters without business logic  
- Configuration classes without behavior
- Generated code (API clients)

#### Mock Authentication for Tests
```csharp
private readonly Mock<ICurrentUserService> _currentUserServiceMock;

// In constructor
_currentUserServiceMock.Setup(x => x.GetCurrentUser())
    .Returns(new CurrentUser("test-id", "Test User", "test@example.com", true));
```

### Integration Testing

#### Test Environment Configuration
- **Authentication**: Always use mock authentication (`UseMockAuth: true`)
- **Database**: Use `Microsoft.AspNetCore.Mvc.Testing` with test database
- **Configuration**: Test-specific appsettings with mock services

#### Integration Test Pattern
```csharp
public class PurchaseOrderEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public PurchaseOrderEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreatePurchaseOrder_WithValidData_ShouldReturn201()
    {
        // Arrange
        var request = new CreatePurchaseOrderRequest { /* valid data */ };
        
        // Act
        var response = await _client.PostAsJsonAsync("/api/purchaseorders", request);
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await response.Content.ReadFromJsonAsync<CreatePurchaseOrderResponse>();
        result.Should().NotBeNull();
    }
}
```

#### Integration Test Coverage (Required)
- **All API Endpoints**: Request/response validation, status codes, error handling
- **Authentication/Authorization**: Endpoint security, user roles
- **Database Operations**: Repository CRUD operations, complex queries
- **Background Jobs**: Hangfire job execution and error handling
- **External Service Integration**: ABRA Flexi, Shoptet API clients

### Test Commands

```bash
# Run all backend tests
cd backend && dotnet test

# Run specific test project
dotnet test backend/test/Anela.Heblo.Tests/

# Run tests with code coverage
dotnet test --collect:"XPlat Code Coverage"

# Run tests in watch mode
dotnet test --watch

# Run specific test method
dotnet test --filter "MethodName"
```

## Frontend Testing (React)

### Technology Stack

- **Unit/Component Tests**: Jest + React Testing Library
- **E2E Tests**: Playwright (automation environment only)
- **Mocking**: MSW (Mock Service Worker) for API calls
- **Test Data**: Factory functions for consistent test data

### Unit & Component Testing

#### Test Structure Pattern
```typescript
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import PurchaseOrderForm from '../PurchaseOrderForm';

describe('PurchaseOrderForm', () => {
  let queryClient: QueryClient;

  beforeEach(() => {
    queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } }
    });
  });

  const renderWithProviders = (component: React.ReactElement) => {
    return render(
      <QueryClientProvider client={queryClient}>
        {component}
      </QueryClientProvider>
    );
  };

  it('should submit form with valid data', async () => {
    // Arrange
    const onSubmit = jest.fn();
    renderWithProviders(<PurchaseOrderForm onSubmit={onSubmit} />);

    // Act
    fireEvent.change(screen.getByLabelText(/supplier/i), { target: { value: 'Test Supplier' } });
    fireEvent.click(screen.getByRole('button', { name: /save/i }));

    // Assert
    await waitFor(() => {
      expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({
        supplierName: 'Test Supplier'
      }));
    });
  });
});
```

#### Component Testing Guidelines

**What to Test (Required)**:
- **Form Interactions**: User input, validation, submission
- **Conditional Rendering**: Show/hide logic based on state or props
- **Event Handling**: Click handlers, form submissions, navigation
- **API Integration**: Success/error states, loading indicators
- **State Management**: Component state changes, prop updates

**What NOT to Test**:
- Implementation details (internal state structure)
- Third-party library functionality
- CSS classes (unless they affect behavior)
- Static content without dynamic behavior

#### Frontend Test Commands
```bash
# Run all frontend unit tests
cd frontend && npm test

# Run tests in watch mode
npm test -- --watch

# Run tests with coverage
npm test -- --coverage

# Run specific test file
npm test PurchaseOrderForm.test.tsx

# Run tests matching pattern
npm test -- --testNamePattern="form validation"
```

## E2E Testing (Playwright)

### Environment Requirements

**CRITICAL**: Playwright tests MUST ALWAYS use the automation environment:
- **Backend Port**: 5001 with `ASPNETCORE_ENVIRONMENT=Automation`
- **Frontend Port**: 3001 with automation configuration
- **Authentication**: Mock authentication ALWAYS enabled
- **Test URL**: `http://localhost:3001` (never port 3000)

### Test Organization Structure

```
frontend/test/
├── ui/                    # UI validation tests
│   ├── layout/           # Layout components (sidebar, responsiveness)
│   ├── auth/             # Authentication flows
│   ├── catalog/          # Catalog functionality
│   ├── purchase/         # Purchase order workflows
│   ├── manufacturing/    # Manufacturing analysis
│   └── analytics/        # Analytics and reporting
├── integration/          # API integration tests
└── e2e/                 # Full user journey tests
```

### Playwright Test Patterns

#### Standard Test Structure
```typescript
import { test, expect } from '@playwright/test';

test.describe('Purchase Order Management', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:3001');
    await page.waitForLoadState('domcontentloaded');
    // Mock auth automatically provides authenticated user
    await page.waitForTimeout(500); // Allow mock auth to complete
  });

  test('should create new purchase order successfully', async ({ page }) => {
    // Navigate to purchase orders
    await page.click('a[href="/purchase-orders"]');
    
    // Click create new order
    await page.click('button:has-text("Create Order")');
    
    // Fill form
    await page.fill('[data-testid="supplier-name"]', 'Test Supplier');
    await page.fill('[data-testid="order-date"]', '2024-12-01');
    
    // Submit form
    await page.click('button[type="submit"]');
    
    // Verify success
    await expect(page.locator('.success-message')).toBeVisible();
    await expect(page.locator('td:has-text("Test Supplier")')).toBeVisible();
  });

  test('should handle form validation errors', async ({ page }) => {
    // Navigate and try to submit empty form
    await page.click('a[href="/purchase-orders"]');
    await page.click('button:has-text("Create Order")');
    await page.click('button[type="submit"]');
    
    // Verify validation errors
    await expect(page.locator('.error-message')).toBeVisible();
    await expect(page.locator('text="Supplier name is required"')).toBeVisible();
  });
});
```

### When to Use E2E Tests

**Required E2E Test Coverage**:
- **Authentication Flow**: Login/logout, session management
- **Critical Business Workflows**: Order creation, inventory updates
- **Responsive Design**: Mobile/tablet/desktop breakpoints
- **Form Interactions**: Complex multi-step forms
- **Navigation**: Sidebar behavior, page routing
- **Error Scenarios**: Network failures, validation errors

**Optional E2E Tests**:
- Minor UI updates that don't affect user workflows
- Simple CRUD operations already covered by integration tests
- Styling changes without functional impact

### Test Execution

#### Manual Test Execution
```bash
# Run all E2E tests (uses automation environment)
./scripts/run-playwright-tests.sh

# Run specific test file
./scripts/run-playwright-tests.sh auth/auth.spec.ts

# Run tests with visible browser (for debugging)
./scripts/run-playwright-tests.sh --headed

# Run tests matching pattern
./scripts/run-playwright-tests.sh --grep "purchase order"
```

#### VS Code Launch Configurations

Available configurations:
1. **"Launch Automation Environment"** - Start both backend (5001) and frontend (3001)
2. **"Run All UI Tests (Playwright)"** - Execute full E2E test suite
3. **"Run UI Tests (Headed)"** - Run tests with visible browser
4. **"Run UI Tests (Debug)"** - Debug mode with Playwright inspector

### Record and Generate Tests
```bash
# Record interactions for new tests
cd frontend && npx playwright codegen localhost:3001

# Install Playwright browsers (run once)
npx playwright install
```

## Environment Configuration

### Port Configuration Matrix

| Environment | Frontend | Backend | Purpose |
|-------------|----------|---------|---------|
| Development | 3000 | 5000 | Hot reload development |
| Automation/Testing | 3001 | 5001 | Playwright E2E tests |
| Test Environment | 8080 | 5000 | Staging container |
| Production | 8080 | 5000 | Production container |

### Authentication Configuration

#### Development/Testing (Mock Auth)
```json
{
  "UseMockAuth": true,
  "Authentication": {
    "MockUser": {
      "Id": "mock-user-id",
      "Name": "Mock User",
      "Email": "mock.user@example.com",
      "Roles": ["admin"]
    }
  }
}
```

#### Production (Real Auth)
```json
{
  "UseMockAuth": false,
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "your-tenant-id",
    "ClientId": "your-client-id"
  }
}
```

### Environment Variables

#### Backend (.NET)
```bash
# Development
ASPNETCORE_ENVIRONMENT=Development

# Testing/Automation
ASPNETCORE_ENVIRONMENT=Automation

# Production
ASPNETCORE_ENVIRONMENT=Production
```

#### Frontend (React)
```bash
# Development (Mock Auth)
REACT_APP_USE_MOCK_AUTH=true
REACT_APP_API_BASE_URL=http://localhost:5000

# Automation (Mock Auth)
REACT_APP_USE_MOCK_AUTH=true
REACT_APP_API_BASE_URL=http://localhost:5001

# Production (Real Auth)
REACT_APP_USE_MOCK_AUTH=false
REACT_APP_API_BASE_URL=https://your-api-domain.com
```

## CI/CD Integration

### GitHub Actions Workflow

The CI pipeline includes:

#### Required Jobs (Must Pass for Merge)
1. **Backend CI**: Unit and integration tests, build validation
2. **Frontend CI**: Unit tests, build, linting
3. **Docker Build**: Production image creation

#### Optional Jobs (Non-Blocking)
4. **UI Tests**: Playwright E2E tests (can fail without blocking merge)

### Quality Gate Requirements

For pull request merge:
- ✅ All backend tests pass
- ✅ All frontend unit tests pass  
- ✅ Build successful (no compilation errors)
- ✅ Code formatting validation (`dotnet format`, `npm run lint`)
- ⚠️ E2E tests (failure doesn't block merge, but should be investigated)

### Auto-Merge Labels

Control merge behavior with labels:
- `auto-merge` - Squash and merge when required checks pass
- `auto-squash` - Explicit squash and merge
- `auto-rebase` - Rebase and merge
- `skip-ui-tests` - Skip Playwright tests entirely

### Test Artifacts

CI automatically uploads:
- Test results and coverage reports
- Playwright test reports and screenshots
- Build artifacts and logs

## Test Commands Reference

### Complete Testing Suite

#### Backend Testing
```bash
# Run all backend tests
cd backend && dotnet test

# Run with code coverage
dotnet test --collect:"XPlat Code Coverage"

# Run specific test category
dotnet test --filter "Category=Unit"
dotnet test --filter "Category=Integration"

# Format code before tests
dotnet format
```

#### Frontend Testing
```bash
# Run unit/component tests
cd frontend && npm test

# Run with coverage
npm test -- --coverage

# Run linting
npm run lint

# Fix linting issues
npm run lint:fix
```

#### E2E Testing
```bash
# Run all Playwright tests
./scripts/run-playwright-tests.sh

# Run specific test file
./scripts/run-playwright-tests.sh auth/auth.spec.ts

# Debug mode with visible browser
./scripts/run-playwright-tests.sh --headed

# Generate new tests by recording
cd frontend && npx playwright codegen localhost:3001
```

### Complete Test Suite Macro

Use the provided macro to run all test types:

```bash
# Run complete test suite (BE, FE, UI, Lint)
# Follows the macro defined in CLAUDE.md:
# - Backend tests (dotnet test)
# - Frontend tests (npm test)
# - UI tests (Playwright)
# - Linting (both BE and FE)
```

## Security & Credentials

### Credential Security Rules

**NEVER commit credentials to source control**:
- ❌ No real passwords, API keys, or authentication secrets
- ❌ No hardcoded credentials in test files
- ❌ No production secrets in configuration files

**Required approach for test credentials**:
- ✅ Use local `.env.test` files (always gitignored)
- ✅ Load credentials via secure utility functions
- ✅ Provide clear setup instructions
- ✅ Tests fail gracefully if credentials are missing

### Safe Credential Loading
```typescript
// frontend/test/auth/test-credentials.ts
export function loadTestCredentials() {
  const envFile = path.join(__dirname, '.env.test');
  if (!fs.existsSync(envFile)) {
    throw new Error('Test credentials file not found. Create .env.test with TEST_EMAIL and TEST_PASSWORD');
  }
  
  const config = dotenv.parse(fs.readFileSync(envFile));
  return {
    email: config.TEST_EMAIL,
    password: config.TEST_PASSWORD
  };
}
```

## Development Workflow Integration

### Test-Driven Development Flow

1. **Red**: Write failing test for new feature/fix
2. **Green**: Implement minimal code to make test pass  
3. **Refactor**: Improve code while keeping tests green
4. **Repeat**: Continue cycle for each requirement

### Pre-Commit Checklist

Before committing changes:
- [ ] All unit tests pass (`dotnet test`, `npm test`)
- [ ] Code formatting is correct (`dotnet format`, `npm run lint`)
- [ ] Integration tests pass for affected modules
- [ ] E2E tests pass for modified UI components
- [ ] No credentials or secrets in code

### PR Review Testing Guidelines

During code review:
- [ ] New features have appropriate test coverage
- [ ] Tests validate business requirements, not just implementation
- [ ] Mock usage is appropriate (external dependencies only)
- [ ] Test names clearly describe the scenario being tested
- [ ] Edge cases and error scenarios are covered

## Best Practices Summary

### Unit Testing
- Use AAA pattern (Arrange, Act, Assert)
- Mock external dependencies only
- Test one scenario per test method
- Use descriptive test method names
- Prefer FluentAssertions over basic asserts

### Integration Testing  
- Use mock authentication in all test environments
- Test complete request/response cycles
- Validate error handling and status codes
- Use test-specific configuration
- Clean up test data appropriately

### E2E Testing
- Focus on critical user journeys only
- Always use automation environment (ports 3001/5001)
- Test responsive behavior across breakpoints
- Include accessibility validation
- Generate tests by recording user interactions

### Maintenance
- Update tests when business requirements change
- Remove tests that no longer provide value
- Refactor test code to eliminate duplication
- Keep test data factories updated
- Monitor test execution time and optimize slow tests

This testing strategy ensures comprehensive coverage while maintaining development velocity and code quality. All team members should reference this document when writing tests or modifying testing infrastructure.