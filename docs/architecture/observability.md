# Observability Strategy - Heblo Application

## Overview

Tento dokument definuje kompletní observability strategii pro Heblo aplikaci, která se skládá z .NET 8 backend API a React frontend hostovaných v Azure. Strategie je navržena pro malý DevOps tým s důrazem na automatizaci a reaktivní monitoring.

## Application Architecture

- **Backend**: .NET 8 App Service (monolith)
- **Frontend**: React aplikace
- **Database**: Azure PostgreSQL Database přes Entity Framework Core
- **External Dependencies**: Proprietární REST API
- **Hosting**: Azure Cloud
- **Environments**: Development (local), Automation (Playwright tests), Test (Azure), Production (Azure)

## Observability Stack

### Core Monitoring Platform
- **Application Insights** - centrální platforma pro všechny telemetry data
- **Sentry** - frontend error tracking a performance monitoring
- **Azure Monitor** - infrastructure monitoring

### Implementation Status
- ✅ **Backend Application Insights** - Fully configured with:
  - Automatic instrumentation for HTTP, database, and external calls
  - Health check endpoints (`/health`, `/health/ready`, `/health/live`)
  - Custom telemetry service for business events
  - Environment-specific configurations
- ⏳ **Frontend Sentry** - Not yet implemented
- ⏳ **Alerting** - Not yet configured

## Telemetry Strategy

### 1. Application Performance Monitoring (APM)

#### Backend (.NET 8)
- **Application Insights SDK** integrace
- **Automatic instrumentation**:
  - HTTP requests/responses
  - Database calls (EF Core)
  - External API calls
  - Exceptions a errors
- **Custom telemetry**:
  - Business event tracking pro kritické operace
  - Performance counters
  - Custom metrics pro business KPIs

#### Frontend (React)
- **Sentry SDK** pro React
- **Monitoring zaměření**:
  - JavaScript errors a unhandled exceptions
  - Performance monitoring (Core Web Vitals)
  - User interactions tracking
  - API call success/failure rates

### 2. Logging Strategy

#### Backend Logging
- **Microsoft.Extensions.Logging** jako primární framework
- **Application Insights** jako centrální log destination
- **Log Levels**:
  - `Critical`: Systém failures, data corruption
  - `Error`: Chyby requiring immediate attention
  - `Warning`: Potenciální problémy, degraded performance
  - `Information`: Business events, audit trail
  - `Debug`: Development/troubleshooting (pouze non-prod)

#### Structured Logging Format
```json
{
  "Timestamp": "2024-07-25T10:30:00Z",
  "Level": "Information",
  "MessageTemplate": "Invoice import completed",
  "Properties": {
    "OperationType": "InvoiceImport",
    "InvoiceId": "INV-12345",
    "ProcessingTimeMs": 1250,
    "RecordCount": 150,
    "UserId": "user123",
    "CorrelationId": "abc-def-ghi"
  }
}
```

#### Frontend Logging
- **Sentry** pro error logging a performance monitoring
- **Custom events** pro kritické user actions
- **Breadcrumbs** pro user journey tracking

### 3. Metrics & KPIs

#### Technical Metrics
- **Response Times**: P50, P95, P99 pro všechny API endpoints
- **Throughput**: Requests per minute/hour
- **Error Rates**: 4xx, 5xx error percentages
- **Database Performance**: Query execution times, connection pool usage
- **External API Health**: Success rates, response times pro proprietární API
- **Infrastructure**: CPU, Memory, Disk usage App Service

#### Business Metrics
- **Kritické operace tracking**:
  - Import faktur: success rate, processing time, volume
  - Import plateb: success rate, processing time, volume  
  - Synchronizace katalogu: success rate, sync duration, record count
- **User Activity**: Active users, session duration
- **Feature Usage**: Usage statistics pro key features

### 4. Distributed Tracing

- **Application Insights Distributed Tracing**
- **Correlation IDs** across all requests
- **End-to-end tracing**:
  - Frontend → Backend API → Database
  - Frontend → Backend API → External API
- **Custom telemetry pro business flows**

## Monitoring & Alerting

### Critical Alerts (Teams Channel)

#### Availability Alerts
- **Application Down**: HTTP 5xx > 5% po dobu 2 minut
- **Database Connectivity**: Connection failures > 3 po dobu 1 minuta
- **External API Failures**: Proprietární API failure rate > 10% po dobu 5 minut

#### Performance Alerts  
- **High Response Time**: P95 response time > 5 sekund po dobu 5 minut
- **High Error Rate**: Overall error rate > 2% po dobu 3 minuty
- **Database Performance**: Avg query time > 2 sekundy po dobu 5 minut

#### Business Process Alerts
- **Import Faktur Failures**: Error rate > 5% po dobu 10 minut
- **Import Plateb Failures**: Error rate > 5% po dobu 10 minut  
- **Synchronizace Katalogu Failures**: Nedokončená synchronizace > 30 minut

#### Infrastructure Alerts
- **High CPU Usage**: > 80% po dobu 10 minut
- **High Memory Usage**: > 85% po dobu 10 minut
- **Low Disk Space**: < 15% free space

### Warning Alerts (Teams Channel)
- **Elevated Response Times**: P95 > 3 sekundy po dobu 10 minut
- **Increased Error Rate**: Error rate > 1% po dobu 10 minut
- **External API Degradation**: Response time increase > 50% po dobu 15 minut

### Health Checks

#### Application Health Endpoints
- `/health` - základní application health
- `/health/ready` - readiness probe pro database connectivity
- `/health/live` - liveness probe pro application responsiveness

#### Synthetic Monitoring
- **Availability Tests** z Application Insights:
  - Production homepage ping test (každých 5 minut)
  - Kritické API endpoints testing (každých 15 minut)
  - Business process simulation (každou hodinu)

### Dashboards

#### Executive Dashboard
- **Application Availability**: Uptime percentage
- **Business Metrics**: Denní/týdenní trendy kritických operací
- **User Activity**: Active users, session metrics
- **Cost Overview**: Azure consumption metrics

#### Operational Dashboard  
- **Real-time Performance**: Response times, throughput, error rates
- **Infrastructure Health**: CPU, Memory, Database performance
- **External Dependencies**: Proprietární API health
- **Recent Incidents**: Alert history, resolved issues

#### Business Dashboard
- **Import Faktur**: Success rates, processing volumes, trends
- **Import Plateb**: Success rates, processing volumes, trends
- **Synchronizace Katalogu**: Sync frequency, record counts, errors
- **Feature Usage**: User engagement metrics

## Implementation Environments

### Development (Local)
- **Application Insights**: Samostatná instance pro development
- **Log Level**: Debug a vyšší
- **Minimal alerting**: Pouze critical errors

### Automation (Playwright)
- **Application Insights**: Sdílená test instance  
- **Synthetic test results** tracking
- **Test execution metrics**

### Test Environment
- **Application Insights**: Dedikovaná test instance
- **Full monitoring stack** (mirror produkce)
- **Alerts disabled** nebo směřované do test Teams kanálu

### Production
- **Application Insights**: Produkční instance s full retention
- **Complete alerting setup**
- **All dashboards active**
- **Data retention**: 90 dní pro detailed logs, 2 roky pro aggregated metrics

## Data Retention & Compliance

### Retention Policies
- **Application Insights Raw Data**: 90 dní
- **Aggregated Metrics**: 2 roky  
- **Custom Logs**: 30 dní
- **Sentry Events**: 90 dní

### Sensitive Data Handling
- **PII Scrubbing**: Automatické odstranění osobních údajů z logů
- **Correlation IDs**: Pro tracking bez exposure citlivých dat
- **Audit Logs**: Separate retention pro compliance (7 let)

## Incident Response Integration

### Alert Escalation
1. **Level 1**: Teams notification
2. **Level 2**: Po 15 minutách bez response - email na DevOps team
3. **Level 3**: Po 30 minutách - management notification

### Runbook Integration
- **Alert annotations** s odkazy na troubleshooting guides
- **Automated remediation** kde možné (restart služeb, clearing cache)
- **Post-incident analysis** tracking v Application Insights

## Future Enhancements

### Phase 2 Considerations
- **Custom Business Intelligence**: Power BI integrace
- **Advanced Analytics**: ML-based anomaly detection
- **Security Monitoring**: Azure Security Center integrace
- **Cost Optimization**: Detailed cost analytics per feature

### Scaling Considerations
- **Multi-region deployment** monitoring
- **Microservices decomposition** observability
- **Advanced distributed tracing**
- **Real User Monitoring (RUM)** enhancement

## Success Metrics

### Operational Excellence KPIs
- **Mean Time To Detection (MTTD)**: < 5 minut
- **Mean Time To Resolution (MTTR)**: < 30 minut pro kritické issues
- **False Positive Rate**: < 5% pro všechny alerty
- **Availability Target**: 99.9% uptime

### Business Impact KPIs  
- **Critical Process Success Rate**: > 98%
- **User Experience**: Error rate < 1%
- **Performance**: P95 response time < 3 sekundy

## Backend Implementation Guide

### Configuration Steps

1. **Add NuGet Packages** (✅ Completed):
   ```xml
   <PackageReference Include="Microsoft.ApplicationInsights.AspNetCore" Version="2.22.0"/>
   <PackageReference Include="AspNetCore.HealthChecks.UI.Client" Version="8.0.1"/>
   <PackageReference Include="AspNetCore.HealthChecks.NpgSql" Version="8.0.1"/>
   <PackageReference Include="System.Diagnostics.DiagnosticSource" Version="8.0.0"/>
   ```
   
   **Note**: The EntityFrameworkCore-specific Application Insights package doesn't exist. EF Core telemetry is automatically captured by the main Application Insights SDK.

2. **Program.cs Configuration** (✅ Completed):
   - Application Insights telemetry with automatic instrumentation
   - Health checks for database connectivity
   - Custom telemetry service registration

3. **AppSettings Configuration** (✅ Completed):
   Each environment has specific configuration:
   - `ApplicationInsights:ConnectionString` - Must be set via Azure secrets
   - `ApplicationInsights:CloudRole` - Environment-specific role name
   - `ApplicationInsights:CloudRoleInstance` - Environment identifier

4. **Custom Telemetry Service** (✅ Completed):
   - `ITelemetryService` interface for business event tracking
   - Methods for tracking:
     - Invoice imports
     - Payment imports
     - Catalog synchronization
     - Order processing
     - Inventory updates

### Required Azure Configuration

1. **Application Insights Resources**:
   - Development: Create AI instance `ai-heblo-dev`
   - Test: Create AI instance `ai-heblo-test`
   - Production: Create AI instance `ai-heblo-production`

2. **Connection Strings**:
   Update Azure Web App settings with AI connection strings:
   - `ApplicationInsights__ConnectionString` for each environment

3. **Alerts Configuration**:
   Configure in Azure Portal → Application Insights → Alerts:
   - Critical alerts (5xx errors, database failures)
   - Business process alerts (import failures, sync delays)
   - Teams channel webhook integration

---

*Tento dokument bude aktualizován při změnách v aplikaci nebo požadavcích na monitoring.*