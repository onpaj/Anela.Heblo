# Controlling Domain

## Overview

The Controlling domain provides business intelligence and compliance monitoring through automated reporting systems. It validates warehouse inventory integrity, monitors business rule compliance, and generates operational insights. The domain implements a pluggable reporting framework that ensures products are stored in appropriate warehouses based on their type classification.

## Domain Architecture

### Service Layer

#### IControllingAppService
Primary service interface providing controlled access to reporting capabilities.

**Core Operations**
```csharp
GenerateReportsAsync() // Execute all registered reports
GetAsync() // Return cached results or generate new ones
```

**Security Features**
- Authorization required for all operations
- Role-based access control
- Audit trail for report generation

#### ControllingAppService
Implementation orchestrating report execution with:
- Dependency injection for all IReport implementations
- Result caching mechanisms
- Error handling and logging
- Background processing integration

### Report Framework

#### IReport Interface
Standardized contract for all reporting implementations:

```csharp
public interface IReport
{
    string Name { get; } // Report identifier
    Task<ReportResult> GenerateAsync(); // Report execution
}
```

#### ReportResult
Result model with factory methods:
- **Success()**: Report completed without issues
- **Failure(message)**: Report identified violations
- **Severity mapping**: Success → Green, Failure → Red

## Report Types

### Warehouse Validation Reports

The system implements three critical warehouse compliance reports:

#### 1. MaterialWarehouseInvalidProductsReport
**Purpose**: Validates Material warehouse integrity
- **Warehouse ID**: 5 (Material warehouse)
- **Valid Product Type**: ProductType.Material (3)
- **Validation Rule**: Only materials should be stored in material warehouse

#### 2. ProductsProductWarehouseInvalidProductsReport
**Purpose**: Validates Product warehouse compliance
- **Warehouse ID**: 4 (Product warehouse)
- **Valid Product Types**: ProductType.Product (8) and ProductType.Goods (1)
- **Validation Rule**: Only finished products and goods allowed

#### 3. SemiProductsWarehouseInvalidProductsReport
**Purpose**: Validates SemiProduct warehouse organization
- **Warehouse ID**: 20 (SemiProduct warehouse)
- **Valid Product Type**: ProductType.SemiProduct (7)
- **Validation Rule**: Only semi-finished products permitted

### Report Logic

#### Validation Algorithm
```csharp
1. Query stock levels for specific warehouse (OnStock > 0)
2. Check product type against warehouse rules
3. Identify misplaced products
4. Generate failure message if violations found
5. Return success if all products properly placed
```

#### Error Message Format
```
"ProductCode1 (Quantity1), ProductCode2 (Quantity2), ..."
```

## Business Rules

### Warehouse-Product Type Mapping

**Material Warehouse (ID: 5)**
- **Allowed**: ProductType.Material only
- **Business Logic**: Raw materials and components storage
- **Violation Impact**: Manufacturing planning inaccuracy

**Product Warehouse (ID: 4)**
- **Allowed**: ProductType.Product and ProductType.Goods
- **Business Logic**: Finished goods ready for sale
- **Violation Impact**: Order fulfillment errors

**SemiProduct Warehouse (ID: 20)**
- **Allowed**: ProductType.SemiProduct only
- **Business Logic**: Work-in-progress storage
- **Violation Impact**: Production workflow disruption

### Compliance Validation

**Stock Level Validation**
- Only validates products with positive stock (OnStock > 0)
- Ignores empty locations to focus on active violations
- Real-time stock data from ERP system

**Type Classification Rules**
- Product type determined by ERP system classification
- Warehouse assignment must match product type
- Violations indicate process failures or data errors

## Data Integration

### FlexiBee ERP Integration

**IStockToDateClient**
- Real-time stock level retrieval
- Warehouse-specific queries
- Current date stock positions
- Product type classification

**Data Flow**
```
FlexiBee ERP → Stock Data → Warehouse Analysis → Compliance Report
```

### Stock Data Processing

**Query Strategy**
- Warehouse-specific stock queries
- Filter by positive stock levels
- Include product type information
- Real-time data retrieval

## Background Processing

### ControllingJob

Automated report generation with comprehensive execution controls.

**Job Configuration**
- **Timeout**: 20 minutes maximum execution
- **Concurrency**: Prevents parallel execution
- **Schedule**: Daily execution (typically 1:00 AM)
- **Error Handling**: Comprehensive exception management

**Execution Flow**
```csharp
1. Check if job is enabled via IJobsAppService
2. Execute GenerateReportsAsync()
3. Cache results for subsequent queries
4. Log execution status and timing
5. Handle errors and notifications
```

### Job Management

**Enable/Disable Controls**
- Administrative control over job execution
- Emergency stop capabilities
- Maintenance mode support

**Monitoring Features**
- Execution timing tracking
- Error rate monitoring
- Success/failure analytics

## Caching Strategy

### Result Caching

**Cache Implementation**
- In-memory storage of report results
- Lazy loading on first access
- Cache invalidation on new generation
- Performance optimization for repeated queries

**Cache Lifecycle**
```
Generate → Cache → Serve → Invalidate → Regenerate
```

### Performance Benefits

- Reduced ERP system load
- Faster dashboard response times
- Consistent data for concurrent users
- Resource optimization

## Monitoring and Alerting

### Compliance Monitoring

**Real-time Validation**
- Continuous warehouse compliance checking
- Immediate violation detection
- Automated alerting systems
- Dashboard status indicators

**Key Performance Indicators**
- Warehouse compliance rate
- Violation trends over time
- Product misplacement frequency
- Resolution time tracking

### Operational Insights

**Business Intelligence**
- Warehouse utilization analysis
- Product flow patterns
- Process improvement opportunities
- Compliance trend analysis

**Decision Support**
- Inventory optimization recommendations
- Warehouse organization improvements
- Process compliance metrics
- Operational efficiency indicators

## Error Handling

### Report Error Management

**Error Categories**
- Data retrieval errors
- Business rule violations
- System connectivity issues
- Validation failures

**Recovery Mechanisms**
- Automatic retry logic
- Graceful degradation
- Error notification systems
- Manual intervention capabilities

### Notification System

**Alert Types**
- Critical compliance violations
- System errors and failures
- Performance degradation warnings
- Maintenance notifications

## Extensibility

### Adding New Reports

**Implementation Pattern**
```csharp
public class NewValidationReport : IReport
{
    public string Name => "NewValidation";
    
    public async Task<ReportResult> GenerateAsync()
    {
        // Implement validation logic
        // Return success or failure with details
    }
}
```

**Registration**
- Automatic discovery via dependency injection
- No configuration changes required
- Immediate availability in report suite

### Framework Benefits

**Pluggable Architecture**
- Easy addition of new report types
- Consistent execution framework
- Standardized result handling
- Uniform error management

## Security and Access Control

### Authentication and Authorization
- Role-based report access
- Secure ERP integration
- Audit trail maintenance
- Data access logging

### Data Protection
- Sensitive data handling
- Secure credential storage
- Encrypted communications
- Compliance documentation

## Business Value

The Controlling domain delivers significant operational benefits:

1. **Compliance Assurance**: Automated validation of business rules and warehouse organization
2. **Operational Efficiency**: Early detection of process violations and inventory issues
3. **Data Quality**: Continuous monitoring of inventory integrity and classification accuracy
4. **Decision Support**: Real-time insights for warehouse management and optimization
5. **Risk Mitigation**: Proactive identification of potential fulfillment and production issues
6. **Automation**: Eliminates manual compliance checking and reduces operational overhead