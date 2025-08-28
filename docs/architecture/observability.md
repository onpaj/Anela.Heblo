# Observability Strategy - Anela Heblo Application

## Overview

Tento dokument definuje kompletní observability strategii pro Anela Heblo aplikaci, která se skládá z .NET 8 backend API a React frontend hostovaných v Azure. Strategie je navržena pro malý DevOps tým s důrazem na **cost-optimized monitoring** a reaktivní alerting.

## Table of Contents
1. [Observability Stack](#observability-stack)
2. [Cost-Optimized Application Insights](#cost-optimized-application-insights)
3. [Telemetry Strategy](#telemetry-strategy)
4. [Logging Strategy](#logging-strategy)
5. [Metrics & KPIs](#metrics--kpis)
6. [Distributed Tracing](#distributed-tracing)
7. [Monitoring & Alerting](#monitoring--alerting)
8. [Environment Configuration](#environment-configuration)
9. [Data Retention & Compliance](#data-retention--compliance)
10. [Incident Response](#incident-response)
11. [Implementation Guide](#implementation-guide)

## Application Context

**Technology Stack**: .NET 8 Backend API + React Frontend  
**Hosting**: Azure Web App for Containers  
**Database**: PostgreSQL  
**External Services**: ABRA Flexi, Shoptet, Comgate  
**Environments**: Development, Automation (testing), Test, Production

## Observability Stack

### Core Components
| Component | Purpose | Status |
|-----------|---------|--------|
| **Application Insights** | Backend telemetry, APM, distributed tracing | ✅ Implemented & Optimized |
| **Custom Telemetry Processors** | Cost optimization via filtering | ✅ Implemented |
| **Health Checks** | Application and database health monitoring | ✅ Implemented |
| **Structured Logging** | Microsoft.Extensions.Logging with AI sink | ✅ Implemented |
| **Business Event Tracking** | Custom telemetry service | ✅ Implemented |
| **Azure Monitor** | Infrastructure monitoring | ⏳ Planned |
| **Frontend Monitoring** | React error tracking | ⏳ Planned |
| **Alerting** | Teams/Email notifications | ⏳ Not configured |

## Cost-Optimized Application Insights

### Cost Reduction Strategy (~60-70% savings)

#### 1. Telemetry Filtering
**CostOptimizedTelemetryProcessor** filters out:
- Health check endpoints (`/health`, `/healthz`, `/ready`, `/live`)
- Static files (JS, CSS, images, fonts)
- Fast successful requests (< 10ms)
- Fast successful dependencies (< 50ms for SQL, < 100ms for HTTP)
- OPTIONS requests (CORS preflight)
- Verbose traces in production

#### 2. Aggressive Sampling
**CustomSamplingTelemetryProcessor** implements:
- Requests: 30% sampling rate
- Dependencies: 10% sampling rate
- Traces: 5% sampling rate
- **Always tracked (100%)**:
  - Exceptions
  - Custom business events
  - Failed requests
  - Slow requests (> 1s)
  - Failed dependencies

#### 3. Environment-Specific Configuration

| Environment | Live Metrics | Perf Counters | Event Counters | Heartbeat | Max Items/sec | Sampling % |
|------------|--------------|---------------|----------------|-----------|---------------|------------|
| **Production** | ❌ Disabled | ❌ Disabled | ❌ Disabled | ✅ Enabled | 5 | 30% (0.1-50%) |
| **Test** | ❌ Disabled | ❌ Disabled | ❌ Disabled | ❌ Disabled | 1 | 10% |
| **Development** | ❌ Disabled | ❌ Disabled | ❌ Disabled | ❌ Disabled | - | NoOp Service |
| **Automation** | ❌ Disabled | ❌ Disabled | ❌ Disabled | ❌ Disabled | - | NoOp Service |

#### 4. Module Control
Disabled expensive modules:
- ❌ `EnableQuickPulseMetricStream` (Live Metrics - 30% cost reduction)
- ❌ `EnablePerformanceCounterCollectionModule` (unless Production)
- ❌ `EnableEventCounterCollectionModule` (unless Production)
- ❌ `EnableDiagnosticsTelemetryModule`
- ❌ `EnableAzureInstanceMetadataTelemetryModule`

### Configuration Files

```json
// appsettings.Production.json
{
  "ApplicationInsights": {
    "ConnectionString": "", // Set via Azure App Service
    "CloudRole": "Heblo-API-Production",
    "EnableLiveMetrics": false,
    "EnablePerformanceCounters": false,
    "EnableEventCounters": false,
    "EnableHeartbeat": true,
    "SamplingSettings": {
      "MaxTelemetryItemsPerSecond": 5,
      "InitialSamplingPercentage": 30,
      "MinSamplingPercentage": 0.1,
      "MaxSamplingPercentage": 50
    }
  }
}
```

## Telemetry Strategy

### Application Performance Monitoring (APM)

#### Backend Telemetry
**Automatic instrumentation captures:**
- HTTP requests/responses with timing
- Database queries (PostgreSQL via EF Core)
- External API calls
- Exceptions with full stack traces
- Dependency tracking

**Custom business events tracked:**
- Invoice and payment processing
- Catalog synchronization
- Order fulfillment
- Inventory updates
- Manufacturing operations
- Transport box tracking

#### Frontend Telemetry (Planned)
- JavaScript error tracking
- Performance monitoring (Core Web Vitals)
- User interaction tracking
- API call success/failure rates

## Logging Strategy

### Log Levels and Usage

| Level | Usage | Example | Sampled in Prod |
|-------|-------|---------|-----------------|
| **Critical** | System failures, data corruption | Database connection lost | Always logged |
| **Error** | Exceptions requiring attention | API call failed | Always logged |
| **Warning** | Potential issues, degraded performance | Retry attempt, slow query | Always logged |
| **Information** | Business events, audit trail | Order processed, user logged in | Sampled |
| **Debug** | Development troubleshooting | Method entry/exit | Filtered out |
| **Trace/Verbose** | Detailed diagnostics | Variable values | Filtered out |

### Structured Logging Format
```csharp
logger.LogInformation("Invoice import completed", new {
    OperationType = "InvoiceImport",
    InvoiceId = invoice.Id,
    ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
    RecordCount = invoice.Lines.Count,
    UserId = currentUser.Id,
    CorrelationId = correlationId
});
```

### Log Configuration
```csharp
// Program.cs
builder.Logging
    .ClearProviders()
    .AddConsole()
    .AddApplicationInsights()
    .AddFilter("Microsoft.AspNetCore", LogLevel.Warning)
    .AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning)
    .AddFilter("Anela.Heblo", LogLevel.Information);
```

## Metrics & KPIs

### Technical Metrics

#### Performance Metrics
- **Response Times**: P50, P95, P99 for all endpoints
- **Throughput**: Requests per minute/hour
- **Error Rates**: 4xx, 5xx percentages
- **Database Performance**: Query execution times, connection pool
- **External API Health**: Availability and response times

#### Resource Metrics
- **CPU Usage**: App Service CPU percentage
- **Memory Usage**: Working set and private bytes
- **Request Queue**: Length and wait time
- **Thread Pool**: Active threads and queued work items

### Business Metrics

#### Critical Operations
| Operation | Success Rate Target | Max Duration | Volume/Day |
|-----------|-------------------|--------------|------------|
| Invoice Import | > 98% | < 30s | ~100 |
| Payment Import | > 99% | < 10s | ~50 |
| Catalog Sync | > 95% | < 5 min | 24 (hourly) |
| Order Processing | > 99% | < 5s | ~200 |
| Box Packaging | > 99% | < 2s | ~500 |

#### User Activity
- Active users per day/week/month
- Session duration and page views
- Feature adoption rates
- API usage by client type

## Distributed Tracing

### Correlation Strategy
- **Operation ID**: Unique identifier for entire request flow
- **Parent ID**: Links related operations
- **W3C Trace Context**: Standard propagation format
- **Custom Correlation**: Business transaction IDs

### Trace Flows
```
User Request → Frontend → Backend API → Database
                              ↓
                        External Services
```

### Correlation Implementation
- Automatic correlation ID propagation via Application Insights
- W3C Trace Context headers for distributed tracing
- Custom business transaction IDs for end-to-end tracking
- All related operations linked through Operation ID and Parent ID

## Monitoring & Alerting

### Health Check Endpoints

| Endpoint | Purpose | Checks | Response Time |
|----------|---------|--------|---------------|
| `/health` | Basic liveness | Application running | < 100ms |
| `/health/ready` | Readiness probe | Database connectivity | < 500ms |
| `/health/live` | Deep health check | All dependencies | < 2s |

### Alert Configuration

#### Critical Alerts (Immediate - Teams + Email)
```yaml
- Application Down: HTTP 5xx > 5% for 2 minutes
- Database Connection Lost: Failures > 3 in 1 minute
- External API Down: Failure rate > 10% for 5 minutes
- High Memory: > 90% for 5 minutes
- Disk Full: < 5% free space
```

#### Warning Alerts (Teams Channel)
```yaml
- High Response Time: P95 > 3s for 10 minutes
- Elevated Error Rate: > 1% for 10 minutes
- Slow Database: Avg query > 1s for 5 minutes
- External API Slow: Response time +50% for 15 minutes
```

#### Business Process Alerts
```yaml
- Invoice Import Failed: Error rate > 5% for 10 minutes
- Payment Import Failed: Any failure in production
- Catalog Sync Stuck: Not completed in 30 minutes
- Low Stock Alert: Critical items < threshold
```

### Synthetic Monitoring
- **Homepage Availability**: Every 5 minutes from 3 regions
- **API Health Check**: Every 10 minutes
- **Business Process Test**: Login → Create Order → Process (hourly)

### Dashboards

#### Executive Dashboard
- Application uptime percentage (target: 99.9%)
- Business KPIs and trends
- Cost metrics and projections
- User satisfaction metrics

#### Operations Dashboard
- Real-time performance metrics
- Active alerts and incidents
- Infrastructure health
- External dependency status

#### Business Dashboard
- Invoice/Payment processing stats
- Catalog synchronization status
- Order fulfillment metrics
- Inventory levels and alerts

## Environment Configuration

### Development (Local)
```json
{
  "ApplicationInsights": {
    "ConnectionString": "", // Empty = NoOpTelemetryService
    "DeveloperMode": true
  },
  "Logging": {
    "LogLevel": {
      "Default": "Debug"
    }
  }
}
```

### Test Environment (Azure)
```json
{
  "ApplicationInsights": {
    "ConnectionString": "InstrumentationKey=test-key",
    "EnableLiveMetrics": false,
    "SamplingSettings": {
      "MaxTelemetryItemsPerSecond": 1,
      "InitialSamplingPercentage": 10
    }
  }
}
```

### Production (Azure)
- Full telemetry with cost optimization
- All business event tracking enabled
- Alerting active with escalation
- 30% sampling rate for non-critical telemetry

## Data Retention & Compliance

### Retention Policies

| Data Type | Development | Test | Production |
|-----------|------------|------|------------|
| Raw Telemetry | N/A | 7 days | 30 days |
| Aggregated Metrics | N/A | 30 days | 2 years |
| Exceptions | N/A | 30 days | 90 days |
| Custom Events | N/A | 30 days | 90 days |
| Audit Logs | N/A | N/A | 7 years (archived) |

### Cost Management
- **Monthly Budget Alert**: $100 for Application Insights
- **Daily Cap**: 1GB/day for Production
- **Sampling**: Aggressive sampling for high-volume, low-value data
- **Archive Strategy**: Export to blob storage after 30 days

### Privacy & Security
- **PII Scrubbing**: Automatic removal from logs
- **Encryption**: TLS in transit, encrypted at rest
- **Access Control**: RBAC with least privilege
- **Audit Trail**: All access logged

## Incident Response

### Alert Escalation Matrix

| Severity | Initial Alert | 15 min | 30 min | 60 min |
|----------|--------------|--------|--------|--------|
| **Critical** | Teams + Email | Phone Call | Manager | Executive |
| **High** | Teams | Email | Manager | - |
| **Medium** | Teams | - | - | - |
| **Low** | Email digest | - | - | - |

### Runbook Integration
Each alert includes:
- Link to troubleshooting guide
- Recent similar incidents
- Suggested remediation steps
- Contact information

### Post-Incident Process
1. **Immediate**: Restore service
2. **24 hours**: Initial incident report
3. **72 hours**: Root cause analysis
4. **1 week**: Preventive measures implemented

## Implementation Guide

### Backend Setup

#### 1. NuGet Packages (✅ Completed)
```xml
<PackageReference Include="Microsoft.ApplicationInsights.AspNetCore" Version="2.22.0"/>
<PackageReference Include="AspNetCore.HealthChecks.NpgSql" Version="8.0.1"/>
<PackageReference Include="AspNetCore.HealthChecks.UI.Client" Version="8.0.1"/>
```

#### 2. Telemetry Configuration (✅ Completed)
```csharp
// Extensions/ApplicationInsightsExtensions.cs
services.AddOptimizedApplicationInsights(configuration, environment);
services.AddApplicationInsightsTelemetryProcessor<CostOptimizedTelemetryProcessor>();
services.AddApplicationInsightsTelemetryProcessor<CustomSamplingTelemetryProcessor>();
```

#### 3. Health Checks (✅ Completed)
Configured health check endpoints for database and external service monitoring.

#### 4. Custom Telemetry Service (✅ Completed)
Business event tracking service implemented for all critical operations.

### Azure Configuration Required

#### 1. Create Application Insights Resources
```bash
# Test Environment
az monitor app-insights component create \
  --app ai-heblo-test \
  --location westeurope \
  --resource-group rg-heblo-test

# Production
az monitor app-insights component create \
  --app ai-heblo-production \
  --location westeurope \
  --resource-group rg-heblo-prod
```

#### 2. Configure App Service Settings
```bash
az webapp config appsettings set \
  --name app-heblo-prod \
  --resource-group rg-heblo-prod \
  --settings ApplicationInsights__ConnectionString="<connection-string>"
```

#### 3. Set Up Alerts
Use Azure Portal or ARM templates to configure alerts based on the alert matrix above.

### Frontend Setup (Planned)

#### 1. Application Insights JavaScript SDK
```typescript
npm install @microsoft/applicationinsights-web
```

#### 2. React Integration
```typescript
// src/telemetry/appInsights.ts
import { ApplicationInsights } from '@microsoft/applicationinsights-web';

const appInsights = new ApplicationInsights({
  config: {
    connectionString: process.env.REACT_APP_AI_CONNECTION_STRING,
    enableAutoRouteTracking: true,
    enableRequestHeaderTracking: true,
    enableResponseHeaderTracking: true
  }
});
```

## Monitoring Checklist

### Daily Checks
- [ ] Application availability > 99.9%
- [ ] No critical alerts in last 24h
- [ ] Response time P95 < 3s
- [ ] Error rate < 1%
- [ ] External API availability

### Weekly Review
- [ ] Telemetry costs within budget
- [ ] Alert noise ratio (false positives)
- [ ] Capacity planning review
- [ ] Incident follow-ups completed

### Monthly Tasks
- [ ] Cost optimization review
- [ ] Retention policy compliance
- [ ] Dashboard accuracy validation
- [ ] Alert threshold tuning
- [ ] Disaster recovery test

## Success Metrics

### Technical KPIs
- **MTTD (Mean Time To Detect)**: < 5 minutes
- **MTTR (Mean Time To Resolve)**: < 30 minutes
- **Availability**: > 99.9% uptime
- **Performance**: P95 < 3 seconds
- **Error Rate**: < 1%

### Business KPIs
- **Process Success Rate**: > 98%
- **Data Processing Time**: Within SLA
- **User Satisfaction**: > 4.5/5
- **Cost per Transaction**: < $0.01

### Operational KPIs
- **Alert Accuracy**: > 95% true positives
- **Automation Rate**: > 80% auto-remediation
- **Documentation Coverage**: 100% for critical paths
- **Runbook Effectiveness**: < 10 min to resolution

---

*Last Updated: December 2024*
*Next Review: March 2025*
*Owner: DevOps Team*