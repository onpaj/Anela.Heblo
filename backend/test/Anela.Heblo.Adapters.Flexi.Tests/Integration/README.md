# FlexiBee Integration Tests

## Setup

To run the integration tests, you need to configure FlexiBee credentials in user secrets.

### 1. Initialize User Secrets (if not already done)

```bash
cd backend/test/Anela.Heblo.Adapters.Flexi.Tests
dotnet user-secrets init
```

### 2. Add FlexiBee Credentials

```bash
dotnet user-secrets set "FlexiBee:Url" "https://your-flexibee-instance.flexibee.eu"
dotnet user-secrets set "FlexiBee:Company" "your-company-code"
dotnet user-secrets set "FlexiBee:Username" "your-username"
dotnet user-secrets set "FlexiBee:Password" "your-password"
```

Alternatively, you can edit the secrets.json file directly at:
- Windows: `%APPDATA%\Microsoft\UserSecrets\399d0992-5041-4ce9-88ae-3a7cdc633453\secrets.json`
- macOS/Linux: `~/.microsoft/usersecrets/399d0992-5041-4ce9-88ae-3a7cdc633453/secrets.json`

Example secrets.json:
```json
{
  "FlexiBee": {
    "Url": "https://your-instance.flexibee.eu",
    "Company": "your-company-code",
    "Username": "your-username",
    "Password": "your-password"
  }
}
```

### 3. Update Test Data

Before running the tests, update the following constants in `FlexiManufactureRepositoryIntegrationTests.cs`:

- `VALID_PRODUCT_ID` - Replace with an actual product ID that has a BoM (Bill of Materials) in your FlexiBee system
- `VALID_INGREDIENT_CODE` - Replace with an actual ingredient code that is used in some products
- `KNOWN_PRODUCT_ID` - Replace with a product ID for the full workflow test

### 4. Run Integration Tests

Remove the `Skip` attribute from the tests you want to run, then:

```bash
dotnet test --filter "FullyQualifiedName~Integration"
```

Or run specific tests:
```bash
dotnet test --filter "FullyQualifiedName~FlexiManufactureRepositoryIntegrationTests"
```

## Notes

- Integration tests are marked with `[Fact(Skip = "...")]` by default to prevent them from running in CI/CD
- These tests connect to a real FlexiBee instance, so they require valid credentials and network connectivity
- The tests use the `FlexiIntegration` collection to ensure they don't run in parallel, preventing potential conflicts
- Make sure your FlexiBee instance has test data that matches the IDs you use in the tests