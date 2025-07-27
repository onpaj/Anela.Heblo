# Bank Domain

## Overview

The Bank domain manages the integration between external payment gateways and the internal accounting system. Its primary responsibility is to automate the import of bank statements from Comgate payment gateway into the FlexiBee accounting system. The domain provides both manual and automated import capabilities, maintains an audit trail of all imports, and supports multiple bank accounts with multi-currency transactions.

## Domain Model

### Core Aggregate: BankStatementImport

The `BankStatementImport` entity serves as the aggregate root for tracking bank statement import operations. It extends `AuditedAggregateRoot<int>` from the ABP framework, providing built-in audit fields.

#### Key Attributes
- **TransferId**: Unique identifier from the Comgate system, ensuring no duplicate imports
- **StatementDate**: The date of the bank statement being imported
- **ImportDate**: Timestamp when the import was performed
- **Account**: Bank account number associated with the statement
- **Currency**: Transaction currency (CZK or EUR)
- **ItemCount**: Number of transactions contained in the statement
- **ImportResult**: Status message - "OK" for success or error description

#### Business Invariants
- Each import must have a unique TransferId
- Currency is automatically determined based on account name suffix ("EUR" indicates EUR currency)
- Import results are immutable once set

### Value Objects

#### CurrencyCode
An enumeration supporting:
- **CZK**: Czech Koruna (default)
- **EUR**: Euro

This value object is shared with the Invoices domain, indicating cross-domain currency handling requirements.

## Business Rules and Processes

### Statement Import Process

The import process follows these steps:

1. **Retrieval Phase**
   - Fetch available transfers from Comgate API for specified date
   - Filter transfers by configured account numbers
   - Check for existing imports to prevent duplicates

2. **Download Phase**
   - Download ABO format files for each new transfer
   - Parse ABO file content for validation

3. **Integration Phase**
   - Import parsed ABO data into FlexiBee accounting system
   - Use mapped FlexiBee account IDs for proper categorization

4. **Recording Phase**
   - Create BankStatementImport record with results
   - Track success/failure status for audit trail

### Currency Assignment Rules
- Accounts with names ending in "EUR" → EUR currency
- All other accounts → CZK currency (default)

### Duplicate Prevention
- TransferId uniqueness prevents re-importing the same statement
- System checks existing imports before processing new ones

## External System Integration

### Comgate Payment Gateway

#### Authentication
- **MerchantId**: Unique merchant identifier
- **Secret**: API authentication secret
- Both configured in ComgateSettings

#### API Endpoints
- **Transfer List**: `https://payments.comgate.cz/v1.0/transferList`
  - Retrieves available transfers for a specific date
  - Returns transfer IDs and basic metadata
  
- **ABO Download**: `https://payments.comgate.cz/v1.0/aboSingleTransfer`
  - Downloads statement in ABO format
  - Requires specific transfer ID

#### ComgateBankClient
Implements `IBankClient` interface with methods:
- `GetAccountStatements(DateTime date, string accountName)`: Fetches and downloads statements
- `GetTransferList(DateTime date)`: Retrieves available transfers
- `DownloadStatement(string transferId)`: Downloads specific ABO file

### FlexiBee Accounting System

#### Integration Approach
- Uses IBankAccountClient from FlexiBee SDK
- Direct ABO format import capability
- Account mapping via FlexiBeeId configuration

#### Account Mapping
Each bank account configuration includes:
- Account name (internal reference)
- Account number (bank account)
- FlexiBeeId (accounting system reference)

## Application Services

### ComgateStatementsAppService

The primary application service managing bank statement operations.

#### Core Operations

**Import Functionality**
```csharp
ImportStatements(string accountName, DateTime statementsDate)
```
- Fetches statements from Comgate
- Imports to FlexiBee
- Records import results
- Returns operation status

**Query Operations**
- Filter by ID, statement date, or import date
- Default sorting by ImportDate (descending)
- Supports pagination via ABP framework

**HTTP Endpoint**
```csharp
ImportStatements(BankImportRequestDto dto)
```
- REST API endpoint for manual imports
- Wraps core import logic with DTO pattern

### Configuration Management

#### BankAccountSettings
Stores bank account configurations:
```json
{
  "Accounts": [
    {
      "Name": "MAIN",
      "Number": "123456789",
      "FlexiBeeId": "BANK-MAIN"
    },
    {
      "Name": "EUR",
      "Number": "987654321",
      "FlexiBeeId": "BANK-EUR"
    }
  ]
}
```

#### ComgateSettings
Stores Comgate API credentials:
- MerchantId
- Secret (encrypted in production)

## Background Jobs

### ComgateDailyImportJob

Automated daily import process for all configured accounts.

#### Job Configuration
- **Job Name Pattern**: "Comgate-Daily ({AccountName})"
- **Schedule**: Daily execution (typically early morning)
- **Arguments**: ComgateDailyImportArgs containing date and account

#### Import Strategy
- **ImportYesterday** method: Imports previous day's statements
- Checks if job is enabled before execution
- Processes all configured accounts sequentially
- Logs results for monitoring

#### Error Handling
- Failed imports are logged but don't stop processing other accounts
- Import results stored in domain entity for troubleshooting
- Administrators notified of persistent failures

## Integration Flow

```
External Systems                 Application Layer              Domain Layer
----------------                 -----------------              ------------
                                                               
Comgate API  ←→  ComgateBankClient  →  ComgateStatementsAppService  →  BankStatementImport
                         ↓                           ↓                          ↓
                  (Download ABO)              (Process Import)           (Store Results)
                         ↓                           ↓
                         └→  FlexiBee API  ←─────────┘
                              (Import ABO)
```

## Use Cases

### Primary Use Cases

1. **Manual Statement Import**
   - User-triggered import for specific date
   - Used for corrections or missing statements
   - Immediate feedback on import status

2. **Automated Daily Import**
   - Background job imports yesterday's statements
   - Runs for all configured accounts
   - Sends notifications on failures

3. **Import History Review**
   - Query past imports by date range
   - Check import status and error messages
   - Audit trail for compliance

### Operational Workflows

1. **Daily Reconciliation**
   - Automated import runs each morning
   - Statements appear in FlexiBee for processing
   - Finance team reviews and reconciles

2. **Error Recovery**
   - Failed imports logged with error details
   - Manual re-import available through UI
   - Support team investigates persistent failures

3. **Multi-Account Management**
   - Each account processed independently
   - Supports different currencies per account
   - Centralized configuration management

## Error Handling and Monitoring

### Error Types
1. **Authentication Failures**: Invalid Comgate credentials
2. **Network Errors**: API unavailability
3. **Data Errors**: Invalid ABO format or missing data
4. **Integration Errors**: FlexiBee import failures

### Monitoring Approach
- Import results stored in database
- Failed imports tracked separately
- Background job health checks
- API availability monitoring

## Security Considerations

1. **Credential Management**
   - API secrets stored in secure configuration
   - No credentials in source code
   - Environment-specific settings

2. **Access Control**
   - Import operations require authorization
   - Read access for viewing history
   - Admin access for configuration

3. **Data Protection**
   - Bank account numbers partially masked in UI
   - Audit trail for all operations
   - Secure communication with external APIs

## Performance Characteristics

1. **Import Speed**: Typically 5-10 seconds per statement
2. **Scalability**: Handles multiple accounts sequentially
3. **Resource Usage**: Minimal CPU/memory requirements
4. **Database Impact**: One record per import, indexed by date

## Business Value

The Bank domain provides critical automation for financial operations:

1. **Time Savings**: Eliminates manual statement downloads and imports
2. **Accuracy**: Reduces human error in data entry
3. **Timeliness**: Daily imports ensure up-to-date financial data
4. **Audit Trail**: Complete history of all import operations
5. **Multi-Currency Support**: Handles international transactions
6. **Integration**: Seamless connection between payment gateway and accounting