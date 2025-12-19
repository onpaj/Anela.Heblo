# Logging Configuration Guide - Kam se co loguje a jak se to konfiguruje

## 📍 Kam se loguje?

Aplikace používá **2 hlavní log destinace**:

### 1. **Console Output** (stdout/stderr)
- **Kde:** Standardní výstup kontejneru
- **Pro:** Development, Docker logs, Azure Container logs
- **Viditelnost:**
  - Development: Přímo v konzoli při `dotnet run`
  - Docker: `docker logs <container-id>`
  - Azure: Azure Portal → Container Logs → Log stream

### 2. **Application Insights** (Azure)
- **Kde:** Azure Application Insights (cloud telemetry service)
- **Pro:** Production monitoring, analytics, alerting
- **Viditelnost:**
  - Azure Portal → Application Insights → Logs
  - Kusto Query Language (KQL) queries
  - Real-time monitoring & dashboards

---

## 🔧 Jak funguje logging infrastructure?

### **Setup v `Program.cs`**

```csharp
// backend/src/Anela.Heblo.API/Program.cs:28
builder.Logging.ConfigureApplicationLogging(builder.Configuration, builder.Environment);
```

### **Implementace v `LoggingExtensions.cs`**

```csharp
public static ILoggingBuilder ConfigureApplicationLogging(...)
{
    // 1. Clear default providers (removes Debug, EventLog, etc.)
    logging.ClearProviders();

    // 2. Add Console logging (for Docker/Azure stdout)
    logging.AddConsole();

    // 3. Load log levels from appsettings.json "Logging" section
    logging.AddConfiguration(configuration.GetSection("Logging"));

    // 4. Add Application Insights (if connection string configured)
    if (!string.IsNullOrEmpty(appInsightsConnectionString))
    {
        logging.AddApplicationInsights(...);
    }

    return logging;
}
```

**Co to znamená:**
- ✅ **Console logs:** Vždy zapnuté (pro všechna prostředí)
- ✅ **Application Insights:** Zapnuté pouze pokud je `ApplicationInsights:ConnectionString` nastaveno
- ✅ **Log levels:** Konfigurovatelné přes `appsettings.json` sekci `Logging`

---

## 📝 Konfigurace Log Levels

### **Aktuální konfigurace (`appsettings.json`)**

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",                                  // Vše ostatní
      "Microsoft.AspNetCore": "Warning",                         // ASP.NET Core framework
      "Microsoft.AspNetCore.Hosting.Diagnostics": "Warning",     // Hosting diagnostics
      "Microsoft.AspNetCore.Routing.EndpointMiddleware": "Warning", // Routing
      "Microsoft.Extensions.Diagnostics.HealthChecks": "None",   // Health checks (vypnuto)
      "Microsoft.AspNetCore.Diagnostics.HealthChecks": "None",
      "Microsoft.IdentityModel": "Error",                        // Identity framework
      "Microsoft.AspNetCore.Authentication": "Warning",          // Authentication
      "Anela.Heblo.API.Infrastructure.Authentication.MockAuthenticationHandler": "Information"
    }
  }
}
```

### **Log Levels vysvětlení**

| Level | Kdy použít | Příklad |
|-------|-----------|---------|
| **Trace** | Velmi detailní debugging (obvykle se nepoužívá v production) | Loop iterations, variable values |
| **Debug** | Debugging info pro development | Method entry/exit, intermediate values |
| **Information** | ✅ **STANDARD** - normální flow aplikace | "Request started", "User logged in", "Import completed" |
| **Warning** | Neočekávané situace, které nejsou errory | "Cache miss", "Retry attempt", "Slow query" |
| **Error** | Chyby, které způsobily selhání operace | Exceptions, failed API calls |
| **Critical** | Katastrofické chyby (app crash) | Database offline, out of memory |
| **None** | Vypnout logování pro daný namespace | Health checks (spam) |

---

## 🎯 Příklady nastavení pro různé komponenty

### **Jak nastavit log level pro tvoje nové komponenty:**

#### **1. Pro nový middleware (RequestLoggingMiddleware):**

```json
{
  "Logging": {
    "LogLevel": {
      "Anela.Heblo.API.Middleware.RequestLoggingMiddleware": "Information"
    }
  }
}
```

**Možnosti:**
- `"Information"` - loguje všechny requesty (default, doporučeno)
- `"Warning"` - loguje pouze error responses (4xx, 5xx)
- `"None"` - vypne middleware logging úplně

#### **2. Pro Comgate Client:**

```json
{
  "Logging": {
    "LogLevel": {
      "Anela.Heblo.Adapters.Comgate.ComgateBankClient": "Information"
    }
  }
}
```

**Možnosti:**
- `"Debug"` - velmi detailní (všechny HTTP requesty + response data)
- `"Information"` - standardní (request start/end, timing) ✅ **Doporučeno**
- `"Warning"` - pouze problémy (HTTP errors, timeouts)

#### **3. Pro Bank Import Handler:**

```json
{
  "Logging": {
    "LogLevel": {
      "Anela.Heblo.Application.Features.Bank.UseCases.ImportBankStatement.ImportBankStatementHandler": "Information"
    }
  }
}
```

#### **4. Pro všechny Adapters najednou:**

```json
{
  "Logging": {
    "LogLevel": {
      "Anela.Heblo.Adapters": "Information"  // Platí pro všechny adapters/*
    }
  }
}
```

#### **5. Pro celou Application layer:**

```json
{
  "Logging": {
    "LogLevel": {
      "Anela.Heblo.Application": "Information"  // Platí pro celou application layer
    }
  }
}
```

---

## 🌍 Environment-specific konfigurace

### **Hierarchie konfiguračních souborů:**

ASP.NET Core načítá konfiguraci v tomto pořadí (později přepisuje dříve):

1. ✅ `appsettings.json` - Base config (všechna prostředí)
2. ✅ `appsettings.{Environment}.json` - Environment override
3. ✅ **User Secrets** (Development pouze) - Lokální tajemství
4. ✅ **Environment Variables** - Docker/Azure config
5. ✅ **Command Line Arguments** - Runtime overrides

### **Příklad: Development vs Production**

#### **`appsettings.Development.json`** (verbose logging pro debugging)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",  // 👈 Více detailů v dev
      "Microsoft.AspNetCore": "Information",  // 👈 Více ASP.NET logů
      "Anela.Heblo": "Debug"  // 👈 Všechny naše komponenty v Debug mode
    }
  },
  "ApplicationInsights": {
    "ConnectionString": ""  // 👈 Vypnuté v dev (loguje se pouze do console)
  }
}
```

#### **`appsettings.Production.json`** (méně noise, Application Insights zapnuté)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",  // 👈 Standard level
      "Microsoft.AspNetCore": "Warning",  // 👈 Méně framework noise
      "Anela.Heblo": "Information"  // 👈 Standardní level
    }
  },
  "ApplicationInsights": {
    "ConnectionString": "InstrumentationKey=xxx;IngestionEndpoint=https://westeurope-5.in.applicationinsights.azure.com/;..."
  }
}
```

#### **`appsettings.Staging.json`** (debugging v staging prostředí)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Anela.Heblo.API.Middleware.RequestLoggingMiddleware": "Information",  // 👈 Detailed request logging
      "Anela.Heblo.Adapters.Comgate": "Debug",  // 👈 Debug Comgate issues
      "Anela.Heblo.Application.Features.Bank": "Debug"  // 👈 Debug bank import
    }
  }
}
```

---

## 🔐 User Secrets (lokální development)

**Pro citlivá data (secrets) v developmentu:**

### **Jak nastavit:**

```bash
# Inicializovat user secrets (už je nastaveno v projektu)
dotnet user-secrets init --project backend/src/Anela.Heblo.API

# Přidat secret
dotnet user-secrets set "ApplicationInsights:ConnectionString" "InstrumentationKey=xxx..." --project backend/src/Anela.Heblo.API
```

### **Kde jsou uloženy:**

- **macOS/Linux:** `~/.microsoft/usersecrets/<user-secrets-id>/secrets.json`
- **Windows:** `%APPDATA%\Microsoft\UserSecrets\<user-secrets-id>\secrets.json`

**User secrets ID pro tento projekt:** `f4e6382a-aefd-47ef-9cd7-7e12daac7e45` (z `.csproj`)

### **Tvoje aktuální user secrets:**

Vidím, že máš user secrets na: `/Users/pajgrtondrej/.microsoft/usersecrets/f4e6382a-aefd-47ef-9cd7-7e12daac7e45/secrets.json`

**Můžeš tam přidat log level overrides:**

```json
{
  "MerchantId": 464081,
  "Logging": {
    "LogLevel": {
      "Anela.Heblo.Adapters.Comgate": "Debug",  // 👈 Local override pro debugging
      "Anela.Heblo.API.Middleware.RequestLoggingMiddleware": "Debug"
    }
  }
}
```

---

## 🐋 Docker / Azure Environment Variables

### **Pro runtime override v Docker nebo Azure:**

#### **Docker Compose:**

```yaml
services:
  api:
    image: anela-heblo:latest
    environment:
      - Logging__LogLevel__Default=Information
      - Logging__LogLevel__Anela.Heblo.Adapters.Comgate=Debug
      - ApplicationInsights__ConnectionString=InstrumentationKey=xxx...
```

**Syntax:**
- Nested JSON → použij `__` (double underscore)
- `"Logging": { "LogLevel": { "Default": "Information" } }` → `Logging__LogLevel__Default=Information`

#### **Azure Web App Configuration:**

```bash
# Azure CLI
az webapp config appsettings set \
  --name heblo \
  --resource-group Anela.Heblo.Production \
  --settings \
    Logging__LogLevel__Default=Information \
    Logging__LogLevel__Anela.Heblo.API.Middleware.RequestLoggingMiddleware=Information \
    Logging__LogLevel__Anela.Heblo.Adapters.Comgate=Debug
```

**Nebo v Azure Portal:**
1. Azure Portal → Web App → Configuration
2. Application Settings → New application setting
3. Name: `Logging__LogLevel__Anela.Heblo.Adapters.Comgate`
4. Value: `Debug`

---

## 📊 Jak vidět logy v různých prostředích

### **1. Development (lokální machine)**

```bash
# Spustit aplikaci
cd backend/src/Anela.Heblo.API
dotnet run

# Logy se zobrazí v konzoli:
# info: Anela.Heblo.API.Middleware.RequestLoggingMiddleware[0]
#       Request START - POST /api/bank-statements/import - ContentType: application/json
```

### **2. Docker (lokální nebo remote)**

```bash
# Zobrazit live logs
docker logs -f <container-id>

# Tail posledních 100 řádků
docker logs --tail 100 <container-id>

# Logs s timestamps
docker logs --timestamps <container-id>

# Filter logs (grep)
docker logs <container-id> 2>&1 | grep "Comgate API"
```

### **3. Azure Container Logs**

**Azure Portal:**
1. Go to: Web App → Monitoring → Log stream
2. Vidíš real-time console output z kontejneru

**Azure CLI:**

```bash
# Stream logs (live)
az webapp log tail --name heblo --resource-group Anela.Heblo.Production

# Download logs
az webapp log download --name heblo --resource-group Anela.Heblo.Production
```

### **4. Application Insights (Azure Analytics)**

**Azure Portal:**
1. Go to: Application Insights → Logs
2. Spusť KQL query:

#### **Query 1: Všechny logy z bank importu (posledních 24 hodin)**

```kql
traces
| where timestamp > ago(24h)
| where message contains "Bank import" or message contains "Comgate API"
| project timestamp, severityLevel, message, customDimensions
| order by timestamp desc
```

#### **Query 2: Request logging middleware (detailed requests)**

```kql
traces
| where timestamp > ago(24h)
| where message contains "Request START" or message contains "Request COMPLETED"
| extend
    Method = customDimensions.Method,
    Path = customDimensions.Path,
    StatusCode = customDimensions.StatusCode,
    Duration = customDimensions.Duration
| project timestamp, severityLevel, Method, Path, StatusCode, Duration, message
| order by timestamp desc
```

#### **Query 3: Comgate API performance**

```kql
traces
| where timestamp > ago(24h)
| where message contains "Comgate API"
| extend Duration = tolong(customDimensions.Duration)
| where Duration > 0
| summarize
    Count = count(),
    AvgDuration = avg(Duration),
    P50 = percentile(Duration, 50),
    P95 = percentile(Duration, 95),
    P99 = percentile(Duration, 99),
    MaxDuration = max(Duration)
  by bin(timestamp, 1h)
| order by timestamp desc
```

#### **Query 4: Errors only (last 7 days)**

```kql
traces
| where timestamp > ago(7d)
| where severityLevel >= 3  // Error and above (Error=3, Critical=4)
| project timestamp, severityLevel, message, customDimensions
| order by timestamp desc
| take 100
```

#### **Query 5: Import success/failure rate**

```kql
traces
| where timestamp > ago(24h)
| where message contains "Bank import COMPLETED"
| extend
    SuccessCount = tolong(customDimensions.SuccessCount),
    ErrorCount = tolong(customDimensions.ErrorCount),
    TotalCount = tolong(customDimensions.TotalCount)
| summarize
    TotalImports = sum(TotalCount),
    TotalSuccess = sum(SuccessCount),
    TotalErrors = sum(ErrorCount)
| extend SuccessRate = (TotalSuccess * 100.0) / TotalImports
```

---

## 🚨 Recommended Configuration pro Production

### **Optimální nastavení pro production monitoring:**

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.AspNetCore.Hosting.Diagnostics": "Warning",
      "Microsoft.Extensions.Diagnostics.HealthChecks": "None",

      // 👇 Tvoje nové komponenty
      "Anela.Heblo.API.Middleware.RequestLoggingMiddleware": "Information",
      "Anela.Heblo.Adapters.Comgate.ComgateBankClient": "Information",
      "Anela.Heblo.Application.Features.Bank": "Information",

      // 👇 Pro troubleshooting (temporary)
      // "Anela.Heblo.Adapters.Comgate": "Debug",  // Uncomment when debugging
      // "Anela.Heblo.API.Middleware.RequestLoggingMiddleware": "Debug"
    }
  },
  "ApplicationInsights": {
    "ConnectionString": "InstrumentationKey=xxx;IngestionEndpoint=https://westeurope-5.in.applicationinsights.azure.com/;LiveEndpoint=https://westeurope.livediagnostics.monitor.azure.com/",
    "CloudRole": "Heblo-API",
    "CloudRoleInstance": "Production"
  }
}
```

---

## 🎛️ Jak dočasně zapnout debug logging v production (bez redeploy)

### **Metoda 1: Azure Portal (doporučeno)**

1. Azure Portal → Web App → Configuration → Application settings
2. Add new setting:
   - **Name:** `Logging__LogLevel__Anela.Heblo.Adapters.Comgate`
   - **Value:** `Debug`
3. Click Save
4. App automaticky restartuje a nahraje novou konfiguraci

**Zpět na Information:**
- Smazat application setting nebo změnit Value na `Information`

### **Metoda 2: Azure CLI**

```bash
# Enable debug logging
az webapp config appsettings set \
  --name heblo \
  --resource-group Anela.Heblo.Production \
  --settings Logging__LogLevel__Anela.Heblo.Adapters.Comgate=Debug

# Disable debug logging (back to default)
az webapp config appsettings delete \
  --name heblo \
  --resource-group Anela.Heblo.Production \
  --setting-names Logging__LogLevel__Anela.Heblo.Adapters.Comgate
```

**⚠️ Warning:** Debug level loguje HODNĚ dat → zvýšené Application Insights costs. Zapnout pouze temporary pro debugging!

---

## 📈 Monitoring & Alerting Setup

### **Application Insights Alerts (doporučené)**

#### **Alert 1: High error rate**

```
Query:
traces
| where severityLevel >= 3
| summarize ErrorCount = count() by bin(timestamp, 5m)
| where ErrorCount > 10

Alert: Když více než 10 errors za 5 minut
```

#### **Alert 2: Slow imports**

```
Query:
traces
| where message contains "Bank import COMPLETED"
| extend Duration = tolong(customDimensions.Duration)
| where Duration > 30000  // 30 seconds
| summarize SlowImports = count() by bin(timestamp, 1h)
| where SlowImports > 0

Alert: Když import trvá více než 30 sekund
```

#### **Alert 3: Import failures**

```
Query:
traces
| where message contains "Bank import COMPLETED"
| extend ErrorCount = tolong(customDimensions.ErrorCount)
| where ErrorCount > 0
| summarize FailedImports = sum(ErrorCount) by bin(timestamp, 1h)
| where FailedImports > 5

Alert: Když více než 5 failed imports za hodinu
```

---

## 🔍 Debugging Checklist

Když potřebuješ debugovat production issue:

1. ✅ **Zkontroluj Azure Container Logs** (real-time)
   - Azure Portal → Log stream

2. ✅ **Zkontroluj Application Insights Logs** (historická data)
   - Application Insights → Logs → KQL queries

3. ✅ **Zapni debug logging pro specific component** (temporary)
   - Azure Portal → Configuration → Add setting: `Logging__LogLevel__{Namespace}=Debug`

4. ✅ **Reprodukuj issue** (trigger import manuálně)
   - Sleduj logs v real-time

5. ✅ **Analyzuj logs** (structured properties)
   - Filter by: TransferId, AccountName, Duration, etc.

6. ✅ **Vypni debug logging** (po debugging)
   - Smazat temporary application setting

---

## 📋 Summary

### **Kde se loguje:**
- ✅ **Console (stdout)** - vždy zapnuté, pro Docker/Azure logs
- ✅ **Application Insights** - zapnuté v production, pro analytics

### **Jak konfigurovat:**
- ✅ **appsettings.json** - base config
- ✅ **appsettings.{Environment}.json** - environment override
- ✅ **User Secrets** - local development secrets
- ✅ **Environment Variables** - Docker/Azure runtime config
- ✅ **Azure Portal** - temporary debug config (bez redeploy)

### **Log levels:**
- ✅ **Debug** - velmi detailní (development nebo temporary debugging)
- ✅ **Information** - standardní production level ⭐ **Doporučeno**
- ✅ **Warning** - neočekávané situace
- ✅ **Error** - chyby
- ✅ **None** - vypnout logování

### **Tvoje nové komponenty:**
- ✅ `Anela.Heblo.API.Middleware.RequestLoggingMiddleware` → `Information`
- ✅ `Anela.Heblo.Adapters.Comgate.ComgateBankClient` → `Information`
- ✅ `Anela.Heblo.Application.Features.Bank` → `Information`

---

**Next steps:**
1. Deploy do staging/production
2. Verify logs v Application Insights
3. Setup alerts pro critical errors
4. Monitor import performance
