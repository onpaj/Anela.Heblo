# Anela.Heblo.Adapters.Shoptet.Tests

Integration tests for Shoptet adapters that connect to a real Shoptet test environment.

## Setup Instructions

### 1. Configure User Secrets

These tests require credentials for a Shoptet test environment. Add them using the .NET User Secrets tool:

```bash
cd /path/to/Anela.Heblo.Adapters.Shoptet.Tests

# Shoptet login credentials
dotnet user-secrets set "Shoptet:Url" "https://your-test-shoptet.myshoptet.com"
dotnet user-secrets set "Shoptet:Username" "your-test-username"
dotnet user-secrets set "Shoptet:Password" "your-test-password"

# Stock export URL
dotnet user-secrets set "ShoptetStockClient:Url" "https://your-test-shoptet.myshoptet.com/action/ExportManager/export/stock"

# Product export URL
dotnet user-secrets set "ProductPriceOptions:ProductExportUrl" "https://your-test-shoptet.myshoptet.com/action/ExportManager/export/products"
```

### 2. Test Environment Requirements

- **Test Shoptet Instance**: Use only a dedicated test environment, never production
- **Valid Test Data**: Ensure the test environment has:
  - Products with stock data
  - Products with pricing information
  - Sample invoices for parsing tests
- **Export Permissions**: Test user must have permissions to access export functions

### 3. Running Tests

```bash
# Run all tests
dotnet test

# Run only unit tests (no external dependencies)
dotnet test --filter Category=Unit

# Run only integration tests (requires test environment)
dotnet test --filter Category=Integration
```

## Test Categories

### Unit Tests
- **XmlIssuedInvoiceParser**: XML parsing and mapping logic
- **CSV Mapping Classes**: CsvHelper mapping configurations
- **Utility Classes**: Helper methods and extensions

### Integration Tests
- **ShoptetStockClient**: CSV stock data import from Shoptet
- **ShoptetPriceClient**: CSV price data import/export to Shoptet
- **Playwright Services**: Browser automation for Shoptet operations

## Important Notes

- All write operations are tested against the test environment only
- Tests include validation of both success and error scenarios
- Playwright tests may take longer due to browser automation
- Some tests may be skipped if test environment is not configured