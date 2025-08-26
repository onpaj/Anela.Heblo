# Transport Box Management User Story

## Feature Overview
The Transport Box Management feature provides lifecycle management for shipping containers throughout the supply chain. This system tracks boxes from creation through delivery, managing inventory movements with proper state validation and complete audit trails while integrating with ERP and e-commerce systems for real-time stock synchronization.

## User Stories

### Primary User Stories

**As a warehouse operator**, I want to manage transport boxes through their complete lifecycle so that I can track inventory movements accurately and maintain audit trails for compliance.

**As a receiving clerk**, I want to receive boxes from both InTransit and Reserve states so that I can process incoming shipments and reserved inventory flexibly.

**As a stock manager**, I want to prevent duplicate box codes in active states so that I can maintain data integrity across the warehouse system.

**As a compliance officer**, I want complete audit trails of all box state changes so that I can track user activities and maintain regulatory compliance.

**As a system integrator**, I want automatic stock synchronization with ERP and e-commerce systems so that inventory levels remain consistent across all platforms.

**As a warehouse operator**, I want to create a new transport box and assign it a unique box number so that I can start filling it with products and track it through the warehouse system.

**As a warehouse operator**, I want to validate box numbers during creation to ensure no duplicate active boxes exist so that I can maintain data integrity and avoid confusion during operations.

**As a warehouse operator**, I want to add products to an open box using autocomplete search so that I can efficiently fill boxes with correct product information and quantities.

**As a warehouse operator**, I want to transition a box to "In Transit" state by confirming its box number so that I can lock the box contents and mark it for shipping.

## Acceptance Criteria

### Core Functionality
1. **Box Lifecycle Management**: System shall support complete box lifecycle from creation to closure
2. **Unique Code Enforcement**: System shall enforce unique box codes across all active transport boxes (excluding Closed/Error states)
3. **State Validation**: System shall prevent invalid state transitions with clear error messages
4. **Audit Trails**: System shall maintain complete audit trails of all state changes with user and timestamp tracking
5. **Item Management**: System shall track individual items within boxes with quantities and user information

### Enhanced Receiving Capabilities
6. **Flexible Receiving**: System shall allow receiving boxes from both InTransit and Reserve states
7. **State Recovery**: System shall support receiving boxes that were previously in Reserve state
8. **Automatic Code Management**: System shall automatically close conflicting boxes when reusing codes

### Data Integrity & Error Handling
9. **Error Recovery**: System shall support comprehensive error logging and recovery mechanisms  
10. **Positive Quantities**: System shall validate that all item amounts are positive values
11. **Required Fields**: System shall enforce required fields (product codes, names, user information)

### Integration Requirements
12. **ERP Synchronization**: System shall synchronize stock levels with ERP systems in real-time
13. **E-commerce Integration**: System shall update e-commerce platform inventory levels
14. **External System Resilience**: System shall handle integration failures gracefully with retry mechanisms

### New Box Creation Workflow
15. **Box Creation Button**: System shall provide "Open New Box" button on box list page
16. **Initial Box State**: System shall create new box with auto-generated ID in "New" state without assigned box number
17. **Box Number Assignment**: System shall allow user to input box number (format: B + 3 digits, e.g., B001, B123)
18. **Box Number Validation**: System shall validate that box number is not already assigned to any active box (not in Closed state)
19. **Duplicate Prevention**: System shall prevent assignment of box numbers already used by active boxes, allowing only closed/completed boxes to have duplicate numbers
20. **State Transition to Opened**: System shall automatically transition box from "New" to "Opened" state upon successful box number assignment
21. **Product Addition Interface**: System shall provide autocomplete search for adding products and materials to opened box
22. **Item Management**: System shall allow adding multiple products/materials with quantities to single box, each item on separate line
23. **Notes Support**: System shall allow adding optional text notes to transport box
24. **Transit Confirmation**: System shall require re-entering box number to confirm transition to "In Transit" state
25. **Box Number Verification**: System shall verify that entered box number matches assigned box number before allowing transit transition
26. **Content Lock**: System shall prevent adding/removing items once box transitions to "In Transit" state

## Business Flow

### New Box Creation Workflow
1. **Box List View**: User clicks "Open New Box" button on transport box list page
2. **Box Creation**: System creates new transport box with auto-generated ID in "New" state (no box number assigned)
3. **Box Number Entry**: User enters box number in format B + 3 digits (e.g., B001, B123)
4. **Number Validation**: System checks that entered box number is not assigned to any active box (boxes in states other than Closed)
5. **Validation Success**: If validation passes, system assigns box number and transitions box to "Opened" state
6. **Validation Failure**: If box number already exists in active state, system shows error message and requires different number
7. **Product Addition**: User can now add products/materials using autocomplete search (product + material per line)
8. **Notes Addition**: User can optionally add text notes to the box
9. **Transit Preparation**: When ready to ship, user enters box number again to confirm transition to "In Transit"
10. **Number Confirmation**: System verifies entered number matches assigned box number
11. **Transit Transition**: Box transitions to "In Transit" state and becomes locked (no more item additions allowed)

### Core Box Lifecycle
1. **New**: Empty box created in system (no box number assigned yet)
2. **Opened**: Box assigned unique code and ready for item management  
3. **InTransit**: Box shipped with tracking information (contents locked)
4. **Received**: Box arrived at destination (can transition from InTransit OR Reserve)
5. **Stocked**: Items added to inventory and available for picking
6. **Closed**: Box lifecycle completed and archived

### Reserve Flow  
1. **Reserve**: Items held for specific orders (accessible from Opened state)
2. **Received**: Reserved items can be received directly (bypassing InTransit)

### Error Handling
- **Error**: Terminal state for boxes with unrecoverable issues (accessible from any state)

## Updated State Transitions

### Allowed State Transitions
- **New** → Opened, Closed
- **Opened** → InTransit, Reserve, New (reset)  
- **InTransit** → Received, Opened (revert)
- **Reserve** → Received, Opened (revert)
- **Received** → Stocked, Closed
- **Stocked** → Closed
- **Any State** → Error

### Removed Transitions
- ❌ **InSwap state removed completely** (no swap operations supported)
- ❌ **ToSwap/ToPick operations removed** (simplified stocking process)
- ❌ **Swap-related state validations removed**

## Business Rules

### Code Management
- **Uniqueness**: No duplicate codes allowed in active states (New, Opened, InTransit, Received, Reserve)
- **Auto-Closure**: System automatically closes existing Stocked boxes when reusing their code
- **Code Assignment**: Codes assigned during transition from New to Opened state and remain immutable
- **Code Format**: Box codes must follow format B + 3 digits (e.g., B001, B123)
- **Validation**: Code required for transit and reserve operations
- **Confirmation Requirement**: Box number must be re-entered to confirm transition to InTransit state

### State Constraints
- **Item Management**: Items can only be added/removed in Opened state
- **Transit Requirements**: Boxes must contain items to transition to InTransit
- **Transit Confirmation**: User must re-enter box number to confirm InTransit transition
- **Content Lock**: Once in InTransit state, no items can be added or removed
- **Receive Sources**: Boxes can be received from InTransit OR Reserve states
- **Terminal States**: Closed and Error are final states with no outbound transitions
- **New State**: New boxes have no assigned code and cannot transition to InTransit until code is assigned

### Audit & Compliance
- **User Tracking**: All state changes must record user identity and timestamp
- **State History**: Complete state transition log maintained for each box
- **Regulatory Compliance**: Audit trail format suitable for compliance reporting
- **Data Retention**: Historical data preserved according to regulatory requirements

## Integration Points

### ERP System Integration
- Real-time stock level synchronization
- Product master data validation
- Transaction logging for financial reconciliation
- Error queuing for failed updates

### E-commerce Platform Integration  
- Inventory availability updates
- Order fulfillment status tracking
- Reserved stock allocation management
- Multi-channel inventory synchronization

### Warehouse Management Integration
- Physical location tracking
- Picking list generation
- Receiving documentation
- Inventory cycle count integration

## Non-Functional Requirements

### Performance
- Support 1000+ concurrent active transport boxes
- Sub-second response times for state transitions
- Handle 100+ items per box efficiently
- Real-time stock synchronization within 2 seconds

### Reliability
- 99.9% uptime for critical box operations
- Automatic retry for integration failures
- Graceful degradation when external systems unavailable
- Complete data recovery capabilities

### Security
- Role-based access control for state transitions
- Audit trail protection from unauthorized modification
- Encrypted sensitive data at rest and in transit
- Secure API endpoints for system integrations

### Scalability
- Horizontal scaling support for multiple warehouses
- Load balancing for high-volume operations
- Efficient database indexing for fast lookups
- Caching strategies for frequently accessed data

## Happy Day Scenario

1. **Box Creation**: User clicks "Open New Box" button to create new transport box in "New" state
2. **Box Number Assignment**: User enters box number (B + 3 digits), system validates uniqueness and assigns code
3. **Box Opening**: System automatically transitions box to "Opened" state upon successful code assignment
4. **Item Addition**: User adds products/materials with quantities using autocomplete search
5. **Notes Addition**: User optionally adds text notes to the box
6. **Transit Confirmation**: User re-enters box number to confirm transition to "InTransit"
7. **Transit Transition**: System verifies box number and marks box as shipped (contents locked)
8. **Receipt Processing**: Receive box at destination warehouse
9. **Stock Integration**: Update inventory levels in all systems
10. **Box Closure**: Complete lifecycle and archive for audit

## Error Handling

### State Validation Errors
- **Invalid Transition**: Clear error message with allowed states
- **Duplicate Code**: Prevent duplicate active box codes with specific error message
- **Invalid Box Number Format**: Validate B + 3 digits format (e.g., B001, B123)
- **Box Number Mismatch**: Verify re-entered box number matches assigned number during transit confirmation
- **Empty Box Transit**: Cannot ship boxes without items
- **State Mismatch**: Validate expected vs actual state
- **Code Not Assigned**: Cannot transition New boxes to InTransit without assigned code

### Data Integrity Errors
- **Missing Product**: Handle unknown product codes gracefully
- **Negative Quantities**: Validate positive amounts only
- **User Tracking**: Ensure all operations have user context
- **Concurrent Updates**: Handle optimistic concurrency

### Integration Errors
- **Stock Update Failures**: Rollback box state on error
- **ERP Sync Issues**: Queue for retry with exponential backoff
- **Network Timeouts**: Implement circuit breaker pattern
- **Partial Success**: Track individual item successes/failures

## Business Rules

### Code Management
1. **Uniqueness**: No duplicate codes in active states (not Closed/Error)
2. **Auto-Closure**: Automatically close stocked boxes when reusing code
3. **Code Validation**: Required for transit operations
4. **Code Immutability**: Cannot change code after opening

### State Constraints
1. **Item Management**: Items only added/removed in Opened state
2. **Transit Requirements**: Must have items to transition to transit
3. **Receive Validation**: Only InTransit/Reserve boxes can be received
4. **Terminal States**: Closed and Error states are final

### Audit Requirements
1. **User Tracking**: All state changes record user and timestamp
2. **Complete History**: Full state transition log maintained
3. **Description Support**: Optional descriptions for context
4. **Compliance Ready**: Audit trail for regulatory requirements

## Persistence Layer Requirements

### Database Schema
```sql
CREATE TABLE TransportBoxes (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Code NVARCHAR(50) NULL,
    State INT NOT NULL,
    DefaultReceiveState INT NOT NULL,
    Description NVARCHAR(MAX) NULL,
    LastStateChanged DATETIME2 NULL,
    Location NVARCHAR(50) NULL,
    CreationTime DATETIME2 NOT NULL,
    CreatorId UNIQUEIDENTIFIER NULL,
    LastModificationTime DATETIME2 NULL,
    LastModifierId UNIQUEIDENTIFIER NULL,
    INDEX IX_TransportBoxes_Code (Code),
    INDEX IX_TransportBoxes_State (State),
    INDEX IX_TransportBoxes_CreationTime (CreationTime)
);

CREATE TABLE TransportBoxItems (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    TransportBoxId INT NOT NULL,
    ProductCode NVARCHAR(50) NOT NULL,
    ProductName NVARCHAR(255) NOT NULL,
    Amount FLOAT NOT NULL,
    DateAdded DATETIME2 NOT NULL,
    UserAdded NVARCHAR(255) NOT NULL,
    FOREIGN KEY (TransportBoxId) REFERENCES TransportBoxes(Id) ON DELETE CASCADE,
    INDEX IX_TransportBoxItems_ProductCode (ProductCode)
);

CREATE TABLE TransportBoxStateLogs (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    TransportBoxId INT NOT NULL,
    State INT NOT NULL,
    Timestamp DATETIME2 NOT NULL,
    UserName NVARCHAR(255) NOT NULL,
    Description NVARCHAR(MAX) NULL,
    FOREIGN KEY (TransportBoxId) REFERENCES TransportBoxes(Id) ON DELETE CASCADE,
    INDEX IX_TransportBoxStateLogs_Timestamp (Timestamp)
);
```

### Caching Strategy
- **Active Box Cache**: Cache frequently accessed active boxes (TTL: 5 minutes)
- **State Machine Cache**: Static configuration cached indefinitely
- **Code Lookup Cache**: Fast duplicate detection (TTL: 1 minute)
- **Invalidation**: Clear cache on state changes

## Integration Requirements

### ERP Integration
- Real-time stock level synchronization
- Product master data validation
- Transaction logging for reconciliation
- Error queue for failed updates

### E-commerce Integration
- Inventory availability updates
- Order fulfillment tracking
- Reserve stock allocation
- Multi-channel inventory sync

### Warehouse Management
- Physical location tracking
- Picking list generation
- Receiving documentation
- Cycle count integration

## Security Considerations

### Access Control
- Role-based permissions for state transitions
- Warehouse-specific access restrictions
- Audit trail protection from tampering
- Sensitive data encryption

### Data Protection
- User activity logging
- State change authorization
- API security for integrations
- Backup and recovery procedures

## Performance Requirements
- Handle 1000+ active transport boxes concurrently
- Sub-second response times for state transitions
- Support 100+ items per box efficiently
- Scale horizontally for multiple warehouses
- Real-time stock synchronization within 2 seconds
- Maintain 99.9% uptime for critical operations