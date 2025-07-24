# 🚀 Azure Deployment Scripts

Tento adresář obsahuje skripty pro nasazení aplikace Anela Heblo do Azure Web App for Containers.

## 📋 Přehled skriptů

### 🎯 Hlavní skripty

- **`deploy.sh`** - Kompletní deployment (build + push + deploy)
- **`build-and-push.sh`** - Build a push Docker image
- **`deploy-azure.sh`** - Nasazení do Azure
- **`azure-utils.sh`** - Utility operace pro management

## 🔧 Příprava před prvním použitím

### 1. Instalace závislostí
```bash
# Azure CLI
curl -sL https://aka.ms/InstallAzureCLIDeb | sudo bash

# Docker (pokud nemáte)
sudo apt-get update
sudo apt-get install docker.io
```

### 2. Přihlášení k službám
```bash
# Azure login
az login

# Docker Hub login
docker login
```

### 3. Konfigurace skriptů
V každém skriptu nahraďte `your-docker-username` vaším skutečným Docker Hub username:

```bash
# Najděte a nahraďte v těchto souborech:
- build-and-push.sh
- deploy-azure.sh
- azure-utils.sh
```

### 4. Nastavení executable práv
```bash
chmod +x scripts/*.sh
```

## 🚀 Použití

### Kompletní deployment (doporučeno)
```bash
# Test prostředí
./scripts/deploy.sh test

# Production prostředí
./scripts/deploy.sh production

# S přeskočením testů (rychlejší)
./scripts/deploy.sh test --skip-tests

# Pouze deploy (bez build)
./scripts/deploy.sh production --skip-build
```

### Pouze build a push
```bash
# Build pro test
./scripts/build-and-push.sh test

# Build pro production
./scripts/build-and-push.sh production
```

### Pouze Azure deploy
```bash
# Deploy test prostředí
./scripts/deploy-azure.sh test

# Deploy production prostředí
./scripts/deploy-azure.sh production
```

### Management operace
```bash
# Zobrazení logů
./scripts/azure-utils.sh logs test

# Status aplikace
./scripts/azure-utils.sh status production

# Restart aplikace
./scripts/azure-utils.sh restart test

# Škálování
./scripts/azure-utils.sh scale-up production B2

# Aktualizace Docker image
./scripts/azure-utils.sh update-image test myuser/anela-heblo:v1.2.3

# Kompletní help
./scripts/azure-utils.sh help
```

## 🌍 Prostředí

### Test Environment
- **Resource Group**: `rg-anela-heblo-test`
- **Web App**: `anela-heblo-test`
- **URL**: `https://anela-heblo-test.azurewebsites.net`
- **SKU**: F1 (Free)
- **Docker Tag**: `test-latest`
- **Mock Auth**: Enabled

### Production Environment
- **Resource Group**: `rg-anela-heblo-prod`
- **Web App**: `anela-heblo`
- **URL**: `https://anela-heblo.azurewebsites.net`
- **SKU**: B1 (Basic)
- **Docker Tag**: `latest`
- **Mock Auth**: Disabled

## 🐳 Docker Images

Skripty vytváří následující Docker images:

```bash
# Test prostředí
your-username/anela-heblo:test-latest
your-username/anela-heblo:test-YYYYMMDD-HHMMSS

# Production prostředí
your-username/anela-heblo:latest
your-username/anela-heblo:vYYYYMMDD-HHMMSS
```

## 🔍 Monitoring a diagnostika

### Health Check
Všechny scripty automaticky testují:
- `/health` endpoint
- Hlavní stránku `/`
- API endpoint `/WeatherForecast`

### Logy
```bash
# Live logy
./scripts/azure-utils.sh logs test

# Stažení logů
./scripts/azure-utils.sh logs-download production

# Kontinuální monitoring
./scripts/azure-utils.sh monitor test
```

### Troubleshooting
```bash
# Zobrazení konfigurace
./scripts/azure-utils.sh config test

# Zobrazení app settings
./scripts/azure-utils.sh settings production

# SSH přístup
./scripts/azure-utils.sh ssh test
```

## 💰 Náklady

### Očekávané náklady
- **Test (F1)**: Zdarma
- **Production (B1)**: ~$13/měsíc
- **Production (B2)**: ~$26/měsíc (při scale-up)

### Monitoring nákladů
```bash
./scripts/azure-utils.sh costs production
```

## 🔐 Bezpečnost

### Credentials
- Azure: Používá `az login` credentials
- Docker Hub: Používá `docker login` credentials
- Žádné credentials nejsou uloženy v scriptech

### App Settings
Skripty automaticky konfigurují:
- `ASPNETCORE_ENVIRONMENT`
- `REACT_APP_API_URL`
- `REACT_APP_USE_MOCK_AUTH`
- `WEBSITES_PORT=8080`
- `WEBSITES_ENABLE_APP_SERVICE_STORAGE=false`

## 🆘 Nouzové postupy

### Rychlý rollback
```bash
# Nasazení předchozí verze
./scripts/azure-utils.sh update-image production your-username/anela-heblo:v20240120-143000
```

### Kompletní reset
```bash
# POZOR: Smaže celé prostředí!
./scripts/azure-utils.sh delete test
./scripts/deploy.sh test  # Vytvoří znovu
```

### Škálování při vysoké zátěži
```bash
./scripts/azure-utils.sh scale-up production B2
# nebo
./scripts/azure-utils.sh scale-up production S1  # Standard tier
```

## 📞 Podpora

Pro problémy se skripty:
1. Zkontrolujte logy: `./scripts/azure-utils.sh logs [env]`
2. Ověřte status: `./scripts/azure-utils.sh status [env]`
3. Restartujte aplikaci: `./scripts/azure-utils.sh restart [env]`

## 🔗 Užitečné odkazy

- [Azure CLI dokumentace](https://docs.microsoft.com/en-us/cli/azure/)
- [Azure Web App for Containers](https://docs.microsoft.com/en-us/azure/app-service/containers/)
- [Docker Hub](https://hub.docker.com/)
- [Azure Cost Management](https://portal.azure.com/#blade/Microsoft_Azure_CostManagement/Menu/costanalysis)