# Stock-Up Process Documentation

## Overview

The **Stock-Up Process** is an automated inventory synchronization system that keeps the Shoptet e-shop inventory in sync with Anela Heblo's internal warehouse operations. It ensures that physical stock changes are immediately reflected in the online shop to prevent overselling and maintain accurate product availability.

## Business Problem

Without automated stock synchronization:
- **Overselling Risk**: The e-shop could sell products no longer in physical stock
- **Manual Entry Errors**: Staff would need to manually update Shoptet inventory
- **Inventory Mismatch**: Online availability wouldn't reflect actual warehouse state
- **Lost Sales**: Out-of-stock items might still show as available, leading to customer dissatisfaction

The Stock-Up Process solves these problems by automatically submitting inventory changes to Shoptet whenever warehouse operations occur.

## Business Triggers

Stock-up operations are triggered by two main business processes:

### 1. Transport Box Receiving (Inbound Logistics)
When materials arrive from suppliers in transport boxes:
- Staff calls **ReceiveTransportBoxHandler** API to mark the box as "Received"
- Handler **immediately creates** StockUpOperations in **Pending state** for each product in the box during the receive operation
- Box state transitions: InTransit/Reserve → **Received**
- Background orchestration service processes these operations asynchronously
- **TransportBoxCompletionService** (background refresh task) runs every 2 minutes to check completion status
- **Only after all** stock-up operations reach **Completed state**, the transport box is marked as "Stocked"
- Shoptet inventory is increased to reflect received materials
- Document number format: `BOX-{boxId:000000}-{productCode}` (e.g., `BOX-000123-PROD001`)

### 2. Gift Package Manufacturing (Production)
When gift packages are manufactured from ingredients:
- Staff executes a gift package manufacture operation
- System creates **negative stock-ups** for consumed ingredients
- System creates **positive stock-ups** for manufactured products
- Shoptet inventory reflects both ingredient consumption and product creation
- Document number format: `GPM-{logId}-{productCode}` (e.g., `GPM-000045-GIFTSET01`)

## End-to-End Business Flow

```
┌─────────────────────────────────────────────────────────────────┐
│ 1. INITIATING EVENT                                             │
│    - Transport Box marked as "Received"                         │
│    - Gift Package Manufacturing executed                        │
└────────────────────┬────────────────────────────────────────────┘
                     │
                     ↓
┌─────────────────────────────────────────────────────────────────┐
│ 2. STOCK-UP OPERATIONS CREATED (in Pending state)               │
│    - System generates unique document number for each item      │
│    - Records product code and quantity change                   │
│    - Links to source operation (box or manufacture)             │
│    - State: Pending                                             │
│    - Transport box STAYS in "Received" state                    │
└────────────────────┬────────────────────────────────────────────┘
                     │
                     ↓
┌─────────────────────────────────────────────────────────────────┐
│ 3. DUPLICATE CHECK (Database Protection)                        │
│    - Check if document number already exists                    │
│    - If exists and completed → SUCCESS (already done)           │
│    - If exists and failed → STOP (requires manual review)       │
│    - If exists and in-progress → STOP (wait for completion)     │
│    - If new → Continue to Shoptet check                         │
└────────────────────┬────────────────────────────────────────────┘
                     │
                     ↓
┌─────────────────────────────────────────────────────────────────┐
│ 4. PRE-SUBMIT SHOPTET CHECK                                     │
│    - Query Shoptet inventory history                            │
│    - If document found → Mark completed (no resubmit needed)    │
│    - If not found → Continue to submission                      │
│    - If check fails → Continue anyway (graceful degradation)    │
└────────────────────┬────────────────────────────────────────────┘
                     │
                     ↓
┌─────────────────────────────────────────────────────────────────┐
│ 5. SUBMIT TO SHOPTET (Browser Automation)                       │
│    - Log into Shoptet admin interface                           │
│    - Navigate to stock management section                       │
│    - Enter document number, product code, and quantity          │
│    - Click "Add to Stock" button                                │
│    - Handle confirmation dialogs                                │
│    - State: Submitted                                           │
└────────────────────┬────────────────────────────────────────────┘
                     │
        ┌────────────┴────────────┐
        ↓                         ↓
    SUCCESS                    FAILURE
        │                         │
        │                         └→ Mark as FAILED, save error
        │                            Stop processing
        ↓
┌─────────────────────────────────────────────────────────────────┐
│ 6. POST-VERIFY IN SHOPTET                                       │
│    - Query Shoptet history again                                │
│    - Verify document actually appears in records                │
│    - If found → Mark as Completed ✓                            │
│    - If not found → Mark as FAILED                              │
│    - If verification fails → Mark as FAILED                     │
└────────────────────┬────────────────────────────────────────────┘
                     │
                     ↓
┌─────────────────────────────────────────────────────────────────┐
│ 7. TRANSPORT BOX COMPLETION (Background Refresh Task)           │
│    - TransportBoxCompletionService runs every 2 minutes         │
│    - Check if ALL stock-up operations for the box are Completed │
│    - If YES → Mark transport box as "Stocked"                   │
│    - If ANY operation Failed → Mark transport box as "Error"    │
│    - If still Pending/Submitted → Keep box in "Received" state  │
└─────────────────────────────────────────────────────────────────┘
```

## Document Number Traceability

Each stock-up operation has a unique document number that links back to its source:

| Source Type | Format | Example | Purpose |
|-------------|--------|---------|---------|
| **Transport Box** | `BOX-{boxId}-{productCode}` | `BOX-000123-PROD001` | Links to specific transport box |
| **Gift Package Manufacture** | `GPM-{logId}-{productCode}` | `GPM-000045-GIFTSET01` | Links to manufacture operation |

This enables:
- **Traceability**: Track exactly what caused an inventory change
- **Duplicate Prevention**: Same document can't be submitted twice
- **Audit Trail**: Complete history of all stock movements
- **Error Recovery**: Easy to identify and retry failed operations

## Four-Layer Protection System

The system implements defense-in-depth to prevent duplicate submissions and ensure reliability:

### Layer 1: Database Unique Constraint
- PostgreSQL enforces `UNIQUE(DocumentNumber)` at database level
- Prevents exact duplicate operations from being created
- Fast check with no external dependencies
- Returns appropriate status: `AlreadyCompleted`, `PreviouslyFailed`, `InProgress`

### Layer 2: Pre-Submit Shoptet Check
- Before submitting, check if document already exists in Shoptet
- If found, mark as completed immediately without resubmitting
- Avoids unnecessary Shoptet API calls
- Gracefully degrades: if check fails, continues with submission

### Layer 3: Transactional Submit
- Mark operation as "Submitted" in database before calling Shoptet
- If process crashes, operation won't be retried without manual intervention
- Provides atomic state transition

### Layer 4: Post-Verify Shoptet Check
- After submission, verify document actually appears in Shoptet history
- Catches "silent failures" where UI appeared successful but didn't persist
- Only marks as "Completed" after verification succeeds
- Failed verification marks operation as FAILED for manual review

## Operation States

Each stock-up operation progresses through states:

```
Pending → Submitted → Completed (SUCCESS)
       ↓
    FAILED (requires manual retry)
```

| State | Meaning | Next Step |
|-------|---------|-----------|
| **Pending** | Created but not yet submitted to Shoptet | Will be processed by orchestration service |
| **Submitted** | Successfully sent to Shoptet via browser automation | Awaiting verification |
| **Completed** | Successfully synchronized and verified in Shoptet | No further action needed |
| **Failed** | Error occurred during processing | Manual retry via UI or API |

**Note**: The operation is only marked as "Completed" after successful verification in Shoptet history. Both verification and completion happen atomically - there is no separate "Verified" state.

## Failure Handling & Recovery

### Common Failure Scenarios

| Failure Type | Cause | What Happens | Recovery |
|--------------|-------|--------------|----------|
| **Submit Failed** | Shoptet unreachable, network error, timeout | Operation marked as FAILED with error message | Manual retry via API |
| **Verification Failed** | Document not found in Shoptet after submit | Operation marked as FAILED | Retry will re-verify |
| **Verification Error** | Playwright timeout during verification check | Operation marked as FAILED | Retry entire process |
| **Duplicate Found** | Document already exists in Shoptet | Operation marked as Completed (idempotent) | No action needed |

### Manual Recovery Process

When operations fail, staff can:

1. **View Failed Operations**
   - Navigate to stock-up management interface
   - Filter by "Failed" state
   - Review error messages to understand the issue

2. **Retry Failed Operation**
   - Select the failed operation
   - Click "Retry" button
   - System resets operation to "Pending" and re-executes entire flow

3. **Monitor Retry**
   - Watch operation progress through states
   - Verify it reaches "Completed" state
   - Check Shoptet inventory matches expected value

## Integration Points

### Shoptet E-shop Integration
The system uses **Playwright** (browser automation) to interact with Shoptet because:
- No official Shoptet API exists for inventory updates
- Must use their admin web interface
- Playwright provides reliable, headless browser automation

**Shoptet Screens Accessed**:
- Login page: `/login`
- Stock management: `/sklad`
- Inventory history: `/sklad/historie`

**What Gets Updated**:
- Product availability counts
- Stock movement history (audit trail in Shoptet)
- Document numbers for tracking

### ABRA Flexi ERP Integration
Indirect integration through:
- Material master data synced from ABRA
- Catalog maintains product codes
- Purchase recommendations influenced by stock levels

### Internal System Integration
**Transport Module**:
- **ReceiveTransportBoxHandler** creates stock-up operations in Pending state when box is received
- **TransportBoxCompletionService** (background refresh task) checks completion status every 2 minutes
- Once all operations are Completed, box is marked as "Stocked"
- If any operation fails, box is marked as "Error"

**Manufacture Module**: When gift packages are manufactured, triggers both positive (products) and negative (ingredients) stock-ups

## Key Technical Components

While this is a business-focused document, here are the main service classes involved:

### Core Services
- **StockUpOrchestrationService**: Main coordinator that implements the 4-layer protection system and orchestrates the entire flow
- **ShoptetPlaywrightStockDomainService**: Bridge to Shoptet, executes browser automation scenarios
- **TransportBoxCompletionService**: Background refresh task (runs every 2 minutes via BackgroundRefreshSchedulerService) that checks if all stock-up operations for received boxes are completed and transitions boxes to "Stocked" state
- **ReceiveTransportBoxHandler**: Creates StockUpOperations immediately when box is received via API call

### Automation Scenarios
- **StockUpScenario**: Playwright script that submits inventory changes to Shoptet admin interface
- **VerifyStockUpScenario**: Playwright script that checks if document exists in Shoptet history

### Domain Entity
- **StockUpOperation**: Represents a single stock change with document number, product code, amount, source, and state
- **TransportBox**: Represents a transport box with state machine (Received → Stocked)

## Monitoring & Observability

### Key Metrics to Track
- **Pending Operations Count**: How many are waiting to be processed
- **Failed Operations Count**: How many require manual intervention
- **Average Processing Time**: From created to completed
- **Failure Rate**: Percentage of operations that fail
- **Retry Success Rate**: Percentage of retries that succeed

### Logs & Audit Trail
- Every state transition is logged
- All failures include detailed error messages
- Pre-check and post-verify results logged
- Complete audit trail of all inventory changes

## API Endpoints

### List Stock-Up Operations
```
GET /api/stock-up-operations?state=Failed&pageSize=50&page=1
```
- Filter by state (Pending, Submitted, Completed, Failed)
- Pagination support
- Returns full operation history with timestamps and errors

### Retry Failed Operation
```
POST /api/stock-up-operations/{id}/retry
```
- Resets operation to Pending state
- Re-executes through entire orchestration flow
- Returns updated operation status

## Data Model (API Response)

When you query stock-up operations, you receive:

```json
{
  "id": 123,
  "documentNumber": "BOX-000123-PROD001",
  "productCode": "PROD001",
  "amount": 100,
  "sourceType": "TransportBox",
  "sourceId": 123,
  "state": "Completed",
  "createdAt": "2026-01-20T10:30:00Z",
  "submittedAt": "2026-01-20T10:31:00Z",
  "completedAt": "2026-01-20T10:32:00Z",
  "errorMessage": null
}
```

**Note**: The `completedAt` timestamp represents the moment when the operation was both verified in Shoptet and marked as completed (these happen atomically).

## Best Practices

### For Staff Using the System
1. **Monitor Failed Operations Daily**: Check for failed stock-ups and retry them
2. **Verify in Shoptet**: Periodically spot-check that Shoptet inventory matches expectations
3. **Document Number Format**: Understand the format to trace operations back to source
4. **Don't Manually Edit Shoptet**: Let the system handle inventory updates to maintain audit trail

### For Developers
1. **Idempotency**: Always design operations to be safe to retry
2. **Error Messages**: Provide detailed error messages for troubleshooting
3. **State Transitions**: Log every state change for audit trail
4. **Graceful Degradation**: If checks fail, continue with submission rather than blocking

## Future Enhancements

Potential improvements to consider:
- **Real-Time Monitoring Dashboard**: Visual display of pending/failed operations
- **Automatic Retry Logic**: Retry failed operations automatically with exponential backoff
- **Shoptet API Integration**: Replace Playwright automation if Shoptet provides official API
- **Batch Processing**: Submit multiple operations in a single Shoptet session
- **Webhook Notifications**: Alert staff immediately when operations fail
- **Performance Metrics**: Track average processing times and identify bottlenecks

## Related Documentation

- **Transport Module**: See `/docs/features/receiving.md` for transport box receiving process
- **Gift Package Manufacturing**: See manufacture module documentation (when available)
- **Shoptet Integration**: See Shoptet adapter documentation for technical details
- **Catalog Sync**: See catalog module for product master data management

---

**Document Version**: 1.3
**Last Updated**: 2026-01-21
**Changes**:
- v1.3: Corrected architecture - TransportBoxCompletionService is background refresh task (not Hangfire job)
- v1.3: Clarified ReceiveTransportBoxHandler creates StockUpOperations during receive operation
- v1.3: Updated document number format to include padding (BOX-{boxId:000000}-{productCode})
- v1.2: Stock-up operations now created immediately when box is received (Pending state), box stays in "Received" until all operations complete
- v1.2: Added CompleteReceivedBoxesJob background job for transitioning boxes to "Stocked" after stock-up completion
- v1.1: Consolidated Verified and Completed states into single Completed state (reflects actual implementation)
- v1.1: Added UI retry functionality specification
**Author**: System Documentation (generated from codebase analysis)
