# E2E Tests for Staging Environment

## Overview

This directory contains end-to-end tests that run against the staging environment using Service Principal authentication with backend override.

## Configuration

Tests use credentials from `/frontend/.env.test`:
- `AZURE_CLIENT_ID` - Service Principal Application ID
- `AZURE_CLIENT_SECRET` - Service Principal Secret
- `AZURE_TENANT_ID` - Azure Tenant ID
- `PLAYWRIGHT_BASE_URL` - Staging environment URL

## Authentication Strategy

Tests authenticate using Service Principal with backend authentication override:
1. Obtain Service Principal token using client credentials flow
2. Set `X-E2E-Test-Token` HTTP header with the token
3. Navigate to staging application
4. Backend middleware validates the Service Principal token
5. Backend creates mock authenticated user for the request
6. Run tests against authenticated application

**Security:** The authentication override only works in Staging environment and validates the Service Principal token before allowing access.

## Running Tests

```bash
# Run all e2e tests
npx playwright test test/e2e/

# Run specific test
npx playwright test test/e2e/staging-auth.spec.ts

# Run with headed browser
npx playwright test test/e2e/ --headed

# Run with debug
npx playwright test test/e2e/ --debug
```

## Test Structure

- `staging-auth.spec.ts` - Main authentication and navigation tests
  - Authentication with service principal
  - Basic navigation verification
  - API call authentication validation

## Setup

1. **Service Principal is pre-configured** in Azure AD with appropriate permissions
2. **Credentials are already set** in `/frontend/.env.test` (gitignored for security)
3. **Backend override is configured** in staging environment only

## Notes

- Tests run against live staging environment using Service Principal authentication
- Backend validates Service Principal tokens before allowing E2E test access
- Authentication override only works in Staging environment for security
- Each test receives fresh authentication via HTTP headers
- Tests focus on application functionality without dealing with login UI flows
- No MFA or user interaction required - fully automated