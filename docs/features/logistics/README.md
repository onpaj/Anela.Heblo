# Logistics Domain

## Overview

The Logistics domain orchestrates the complete supply chain operations from warehouse receiving to order fulfillment. It manages transport containers, picking operations, inventory reconciliation, and carrier integrations. The domain implements sophisticated state management for transport boxes, automated order fulfillment through web automation, and real-time inventory synchronization across multiple systems including ERP and e-commerce platforms.

## Domain Model

### Core Aggregates

#### TransportBox

The primary aggregate root managing shipping containers and their complete lifecycle. Transport boxes are the fundamental unit for tracking inventory movement through the warehouse.

**Key Attributes**

**Identification**
- **Code**: Unique transport box identifier
- **DeliveryType**: Shipping method used
- **TrackingNumber**: Carrier tracking identifier
- **UserId**: User responsible for box operations

**State Management**
- **State**: Current box state (finite state machine)
- **StateHistory**: Complete audit trail of state changes
- **OpenUserId**: User who opened the box
- **ReceivedUserId**: User who received the box

**Physical Properties**
- **Weight**: Total box weight
- **Items**: Collection of contained products
- **Location**: Physical warehouse location

**Temporal Tracking**
- **CreatedDate**: Box creation timestamp
- **OpenedDate**: When box was opened
- **TransitDate**: When box entered transit
- **ReceivedDate**: When box was received
- **ClosedDate**: When box was finalized

#### TransportBoxItem

Represents individual products within a transport box.

**Key Attributes**
- **ItemId**: Unique item identifier
- **ProductCode**: Product identifier
- **Quantity**: Item quantity
- **State**: Item-specific state
- **ItemType**: Type classification
- **UserId**: User who added the item

#### TransportBoxStateLog

Audit trail entity tracking all state changes.

**Key Attributes**
- **PreviousState**: State before transition
- **CurrentState**: State after transition
- **UserId**: User who triggered the change
- **Timestamp**: When change occurred
- **Note**: Optional change description

### Value Objects and Enumerations

#### TransportBoxState

Finite state machine states defining box lifecycle:

- **New**: Initial state for newly created boxes
- **Opened**: Box is accessible for item management
- **InTransit**: Box is being shipped
- **Received**: Box has arrived at destination
- **InSwap**: Temporary state for inventory reconciliation
- **Stocked**: Items are available for order fulfillment
- **Reserve**: Items held for specific orders
- **Closed**: Final state, box lifecycle complete
- **Error**: Exception state with error logging

#### Carriers

Supported shipping providers:
- **Zasilkovna**: Czech Republic pickup point network
- **PPL**: Professional parcel logistics
- **GLS**: General logistics systems
- **Osobak**: Personal pickup

#### Warehouses

Warehouse type classification:
- **Product**: Finished goods warehouse
- **Material**: Raw materials storage
- **SemiProduct**: Work-in-progress storage

#### StockSeverity

Inventory urgency levels:
- **None**: Adequate stock levels
- **Minor**: Monitor closely
- **Major**: Action needed soon
- **Critical**: Immediate action required

### Supplier Entity

**Key Attributes**
- **Code**: Supplier identifier
- **Name**: Supplier display name
- **IsPrimary**: Primary supplier flag

## State Machine Implementation

### Transport Box Lifecycle

The transport box implements a sophisticated state machine ensuring proper workflow progression:

```
New → Opened → InTransit → Received → (InSwap/Stocked) → Closed
  ↘               ↘              ↗              ↗
   Reserve        Error ←────────┘              │
     ↘                                         │
      └── (can transition to any state) ──────┘
```

### State Transition Rules

1. **New → Opened**: Box must have unique code in active states
2. **Opened → InTransit**: Box must contain at least one item
3. **InTransit → Received**: Only allowed when box is in transit
4. **Received → InSwap**: For inventory reconciliation
5. **Received → Stocked**: Direct stocking of received items
6. **InSwap → Stocked**: After reconciliation completion
7. **Any → Reserve**: Items held for specific orders
8. **Any → Error**: Exception handling with error logging
9. **Any → Closed**: Final state (with validation)

### Business Rules

1. **Code Uniqueness**: No duplicate codes in active states (not Closed/Error)
2. **Auto-Closure**: When reopening box with existing code, previous box auto-closes
3. **Item Constraints**: Items can only be added in Opened state
4. **User Tracking**: All state changes track responsible user
5. **Audit Trail**: Complete history maintained for compliance

## Application Services

### TransportBoxAppService

Primary service managing transport box operations with comprehensive functionality.

#### Core Operations

**Lifecycle Management**
```csharp
OpenBoxAsync(code, userId) // Open new or reuse existing box
TransitBoxAsync(boxId, deliveryType, trackingNumber) // Send to transit
ReceiveBoxAsync(boxId, weight) // Mark as received
CloseBoxAsync(boxId) // Finalize box
```

**Item Management**
```csharp
AddItemsAsync(boxId, items) // Add products to box
RemoveItemAsync(boxId, itemId) // Remove specific item
UpdateItemQuantityAsync(boxId, itemId, quantity) // Modify quantities
```

**Advanced Operations**
```csharp
StockUpBoxAsync(boxId) // Move received items to stock
SwapInAsync(boxId) // Start inventory reconciliation
ReserveAsync(boxId, reservationData) // Hold items for orders
```

#### Business Logic Features

1. **Duplicate Prevention**: Validates unique codes across active boxes
2. **Auto-Closure**: Automatically closes conflicting boxes
3. **Stock Integration**: Real-time catalog stock updates
4. **Background Processing**: Async operations for heavy tasks
5. **Error Handling**: Comprehensive exception management

### PickingAppService

Manages order fulfillment and expedition list generation.

#### Core Capabilities

**Expedition Management**
```csharp
GenerateExpeditionListAsync(date, carrier) // Create picking lists
PrintExpeditionListAsync(expeditionData) // Output to print queue
SendExpeditionEmailAsync(listData) // Email via SendGrid
```

**Daily Operations**
- **PrintPickingListDailyJob**: Automated daily expedition generation
- **Multi-Carrier Processing**: Handles different shipping providers
- **Email Integration**: Automatic distribution of picking lists

#### Shoptet Integration

**Automated Web Operations**
- Playwright-based browser automation
- Secure authentication to Shoptet admin
- Batch order processing
- PDF generation and download
- Automatic order state transitions

**Carrier-Specific Processing**
- Zasilkovna: Pickup point orders
- PPL: Professional logistics
- GLS: Express delivery
- Osobak: Personal pickup

### WarehouseStockTakingAppService

Handles inventory management and warehouse operations.

#### Stock Management Operations

**Inventory Counts**
```csharp
SubmitStockTakingAsync(stockData) // Physical inventory submission
GetWarehouseStockAsync(query) // Current inventory levels
TrackProductMovementAsync(productCode) // Movement history
```

**Synchronization Features**
- **ERP Integration**: FlexiBee stock level updates
- **E-shop Sync**: Shoptet inventory synchronization
- **Real-time Updates**: Immediate stock level changes
- **Batch Operations**: Efficient bulk processing

## External System Integration

### Shoptet E-commerce Platform

#### Automated Operations via Playwright

**Picking List Processing**
1. **Authentication**: Secure login to Shoptet admin panel
2. **Order Selection**: Filter and select orders by date/carrier
3. **Batch Processing**: Handle multiple orders simultaneously
4. **PDF Export**: Generate expedition lists as downloadable PDFs
5. **State Updates**: Automatically update order statuses

**Stock Management Operations**
1. **Stock Taking**: Direct inventory updates via web interface
2. **Product Search**: Automated product lookup and selection
3. **Reserve Management**: Handle both free and reserved stock
4. **Document Creation**: Generate stock documents with tracking IDs

**Stock-Up Operations**
1. **Inventory Addition**: Process incoming stock receipts
2. **Product Validation**: Verify product codes and quantities
3. **Document Generation**: Create stock-up records
4. **Integration**: Sync with transport box system

#### Web Automation Architecture

**Playwright Configuration**
- Headless browser automation
- Secure credential management
- Error handling and retry logic
- Screenshot capture for debugging

**Session Management**
- Persistent login sessions
- Automatic session refresh
- Concurrent operation handling
- Resource cleanup

### ERP System Integration

#### FlexiBee Integration
- Stock level synchronization
- Product master data access
- Document generation
- Audit trail maintenance

#### Real-time Operations
- Immediate stock updates
- Inventory reconciliation
- Error reporting
- Transaction logging

## Business Processes and Workflows

### Inbound Logistics Workflow

1. **Receipt Planning**
   - Create new transport box
   - Assign unique tracking code
   - Set expected delivery date

2. **Receiving Process**
   - Open transport box
   - Add received items with quantities
   - Verify against expected delivery
   - Transition to InTransit state

3. **Arrival Processing**
   - Mark box as Received
   - Validate item quantities
   - Update weight information
   - Prepare for stocking

4. **Inventory Processing**
   - Stock items into catalog
   - Update ERP system
   - Sync e-shop inventory
   - Close transport box

### Outbound Logistics Workflow

1. **Order Processing**
   - Generate daily expedition lists
   - Filter orders by carrier
   - Create picking lists
   - Distribute to warehouse staff

2. **Fulfillment Execution**
   - Pick items from stocked boxes
   - Verify quantities and products
   - Update box item states
   - Prepare for shipping

3. **Shipping Coordination**
   - Process through carrier systems
   - Update tracking information
   - Notify customers
   - Update order states

### Inventory Management Workflow

1. **Stock Taking Preparation**
   - Schedule inventory counts
   - Generate count sheets
   - Assign counting teams
   - Suspend normal operations

2. **Count Execution**
   - Physical inventory counting
   - Lot tracking validation
   - Expiration date verification
   - Discrepancy identification

3. **Reconciliation Process**
   - Compare physical vs system counts
   - Investigate variances
   - Approve adjustments
   - Update all systems

## Performance and Scalability

### Optimization Strategies

1. **Background Processing**
   - Async operations for heavy tasks
   - Job queuing for batch operations
   - Non-blocking user interfaces
   - Efficient resource utilization

2. **Database Optimization**
   - Proper indexing strategies
   - Query optimization
   - Connection pooling
   - Transaction management

3. **Caching Mechanisms**
   - Memory caching for frequently accessed data
   - Session state management
   - Reduced database round trips
   - Improved response times

### Scalability Features

- **Horizontal Scaling**: Supports multiple warehouse locations
- **Concurrent Operations**: Thread-safe state management
- **Load Distribution**: Background job processing
- **Resource Management**: Efficient memory and connection usage

## Error Handling and Monitoring

### Error Management

1. **State Validation**: Prevents invalid state transitions
2. **Exception Logging**: Comprehensive error tracking
3. **Recovery Mechanisms**: Automatic retry capabilities
4. **User Notification**: Clear error messages and guidance

### Monitoring and Audit

1. **Complete Audit Trail**: All operations logged with user tracking
2. **State Change History**: Full lifecycle tracking
3. **Performance Monitoring**: Operation timing and efficiency
4. **Compliance Reporting**: Regulatory audit support

## Security and Access Control

### Authentication and Authorization
- Role-based access control
- User activity tracking
- Secure credential storage
- Session management

### Data Protection
- Audit trail integrity
- Secure external communications
- Credential encryption
- Access logging

## Configuration and Setup

### Transport Configuration
- Carrier settings and credentials
- Warehouse location mapping
- State machine rules
- User permissions

### Integration Settings
- Shoptet connection parameters
- ERP system endpoints
- Email service configuration
- Print queue settings

## Business Value

The Logistics domain delivers significant operational benefits:

1. **Operational Efficiency**: Automated workflows reduce manual effort and errors
2. **Inventory Accuracy**: Real-time synchronization maintains accurate stock levels
3. **Order Fulfillment**: Streamlined picking and shipping processes
4. **Compliance**: Complete audit trails for regulatory requirements
5. **Scalability**: Supports business growth with efficient resource utilization
6. **Integration**: Seamless connection between warehouse and e-commerce operations