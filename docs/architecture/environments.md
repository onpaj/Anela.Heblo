# 🌍 Environment Configuration Documentation

This document defines all environment configurations, port mappings, deployment architectures, and Azure Web App settings for the application.

---

## 🌍 Environment Definition

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

---

## 🔗 CORS and Azure AD Redirect URI Configuration

### CORS Policy Configuration:

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

### Azure AD Redirect URI Configuration:

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

### Implementation Notes:

1. **Single Origin Strategy**: Container environments use same origin for frontend and backend, eliminating CORS issues
2. **Development CORS**: Only needed for local development with separate servers
3. **Dynamic URLs**: Azure may append random suffixes to web app names - update redirect URIs accordingly
4. **HTTPS Required**: Azure AD requires HTTPS for production redirect URIs
5. **Callback Routes**: Optional - MSAL can handle auth without explicit callback routes

---

## 🏗️ Deployment Architecture:

**Development/Debug**: 
- **Frontend**: Standalone React dev server (`npm start`) with **hot reload** (localhost:3000)
- **Backend**: ASP.NET Core dev server (`dotnet run`) (localhost:5000)
- **Architecture**: **Separate servers** to preserve hot reload functionality for development
- **CORS**: Configured for cross-origin requests between frontend and backend
- **Authentication**: Mock authentication enabled for both frontend and backend
- **Why separate**: Hot reload requires React dev server - single container would disable this critical development feature

**Local Docker Testing**:
- **Single Docker container** running on port 8080
- Container serves both React static files and ASP.NET Core API
- URL: `http://localhost:8080`
- Used for testing production-like environment locally

**Azure Test Environment**:
- **Single Docker container** hosted on **Azure Web App for Containers**
- **Resource Group**: `rgHeblo`
- **Web App**: `heblo-test` (Azure may append random suffix)
- **URL**: `https://heblo-test.azurewebsites.net` (actual URL may vary)
- **Container Port**: 8080
- **Authentication**: Mock authentication enabled

**Azure Production Environment**: 
- **Single Docker container** hosted on **Azure Web App for Containers**
- **Resource Group**: `rgHeblo` (shared with test)
- **Web App**: `heblo` (Azure may append random suffix)
- **URL**: `https://heblo.azurewebsites.net` (actual URL may vary)
- **Container Port**: 8080
- **Authentication**: Azure AD (Microsoft Entra ID)
- Container serves both React static files and ASP.NET Core API
- URL: `https://anela-heblo.azurewebsites.net`
- Real Microsoft Entra ID authentication

---

## 🚀 Azure Web App for Containers Deployment

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

## 🔐 Environment-Specific CI/CD Configuration

### Environment-Specific App Settings
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

## 🏛️ Development vs Production Architecture:
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

## 📊 Environment Summary

This document covers:

- **Port configurations** for all environments (local, automation, test, production)
- **Environment variables** and configuration files for each environment
- **CORS and authentication** setup across different deployment strategies
- **Azure Web App** infrastructure and configuration
- **Deployment architectures** from development to production
- **Environment-specific settings** for CI/CD automation