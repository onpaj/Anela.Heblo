# Product Requirements Document: Manufacturing Stock Movement Refactoring

## 1. Executive Summary

**Project Name:** Manufacturing Stock Movement Refactoring  
**Version:** 1.0  
**Date:** 2026-02-03  
**Status:** Draft

### Overview
Refactor the manufacturing process in Anela Heblo to use FlexiBee's stock movement APIs (`IStockMovementClient` and `IStockItemsMovementClient`) instead of the legacy manufacturing order flow. This change is driven by updates in the FlexiBee system that require adaptation.

### Business Justification
- **Driver:** FlexiBee system changes (external dependency)
- **Priority:** Urgent (production dependency)
- **Goal:** Maintain feature parity while adapting to new FlexiBee APIs

### Success Criteria
- Manufacturing workflow maintains existing functionality
- All existing validations preserved (material availability, quantities)
- Historical manufacturing orders remain readable
- Zero UI changes required
- Unit test coverage maintained

---

## 2. Problem Statement

### Current State
The current manufacturing implementation uses `FlexiManufactureClient.SubmitManufactureAsync()` which:
1. Creates a new manufacturing order
2. Loads manufacturing order details
3. Finalizes the manufacturing order

This legacy approach is no longer compatible with FlexiBee system changes.

### Desired State
Replace the manufacturing order flow with direct stock movements:
1. Create **consumption stock movement** (raw materials → manufacturing)
2. Create **production stock movement** (manufacturing → finished goods)
3. Both movements linked to single manufacturing operation
4. No finalize step required (movements are immediately final)

### Constraints
- Must use latest stable `Rem.FlexiBee` NuGet packages
- Cannot guarantee atomicity between two movements (manual rollback on errors)
- Backend implementation only - no UI changes
- Must preserve batch/lot tracking from existing system

---

## 3. Scope

### In Scope
✅ Refactor `SubmitManufactureAsync` implementation  
✅ Integrate `IStockMovementClient` and `IStockItemsMovementClient`  
✅ Create two stock movements per manufacturing operation (consume + produce)  
✅ Upgrade to latest stable `Rem.FlexiBee` NuGet packages  
✅ Maintain existing validations (materials, quantities)  
✅ Preserve batch/lot number tracking  
✅ Unit test updates for new implementation  
✅ Error handling for failed movements (manual intervention required)  

### Out of Scope
❌ UI changes (manufacturing module remains unchanged)  
❌ Historical data migration (old orders stay as-is)  
❌ New backend handlers or services  
❌ Atomic transaction guarantees between movements  
❌ Automated rollback mechanisms  
❌ Integration/E2E tests (unit tests only)  
❌ Changes to manufacturing workflow or user experience  

### Future Considerations
- Automated retry mechanisms for failed movements
- Enhanced error recovery workflows
- Performance optimizations for bulk manufacturing operations

---

## 4. Requirements

### 4.1 Functional Requirements

#### FR-1: Stock Movement Creation
**Priority:** P0 (Critical)

**Description:**  
Replace manufacturing order creation with two stock movements per operation.

**Acceptance Criteria:**
- ✅ Consumption movement created first (raw materials → WIP)
- ✅ Production movement created second (WIP → finished goods)
- ✅ Both movements reference same manufacturing batch/operation
- ✅ Movements use latest `IStockMovementClient` and `IStockItemsMovementClient` APIs

---

#### FR-2: Material Validation
**Priority:** P0 (Critical)

**Description:**  
Maintain all existing validations for manufacturing operations.

**Acceptance Criteria:**
- ✅ Raw material availability checked before consumption movement
- ✅ Quantity validations match existing logic
- ✅ Batch/lot number tracking preserved
- ✅ Business rule validations unchanged

---

#### FR-3: Error Handling
**Priority:** P0 (Critical)

**Description:**  
Handle failures in stock movement creation with manual intervention.

**Acceptance Criteria:**
- ✅ Clear error messages for consumption movement failures
- ✅ Clear error messages for production movement failures
- ✅ Partial success states logged (e.g., consumption succeeded, production failed)
- ✅ Error details include movement IDs for manual rollback
- ✅ No automatic retry or rollback (operator must fix manually)

---

#### FR-4: Historical Data Preservation
**Priority:** P1 (High)

**Description:**  
Keep existing manufacturing orders readable without migration.

**Acceptance Criteria:**
- ✅ Old manufacturing orders remain queryable
- ✅ No data migration required
- ✅ Historical reports continue to work

---

### 4.2 Non-Functional Requirements

#### NFR-1: API Compatibility
**Priority:** P0 (Critical)

**Requirement:**  
Use latest stable version of `Rem.FlexiBee` NuGet packages.

**Acceptance Criteria:**
- ✅ All FlexiBee package references updated to latest stable
- ✅ Breaking changes from package upgrade addressed
- ✅ No deprecated API usage

---

#### NFR-2: Code Quality
**Priority:** P1 (High)

**Requirement:**  
Maintain clean architecture and testability.

**Acceptance Criteria:**
- ✅ Follow existing Clean Architecture patterns
- ✅ No new handlers/services (refactor existing `SubmitManufactureAsync`)
- ✅ Unit tests updated to cover new implementation
- ✅ Code passes `dotnet format` validation

---

#### NFR-3: Backward Compatibility
**Priority:** P0 (Critical)

**Requirement:**  
No breaking changes to API contracts or UI.

**Acceptance Criteria:**
- ✅ API endpoints unchanged
- ✅ Request/response DTOs unchanged
- ✅ UI components require zero modifications
- ✅ Existing integrations continue to work

---

## 5. Technical Design

### 5.1 Architecture Overview

**Current Flow:**
```
SubmitManufactureAsync()
  └─> FlexiManufactureClient.CreateManufactureOrder()
      └─> Load details
      └─> Finalize order
```

**New Flow:**
```
SubmitManufactureAsync()
  ├─> IStockMovementClient.CreateConsumptionMovement()
  │   └─> Raw materials → WIP
  └─> IStockMovementClient.CreateProductionMovement()
      └─> WIP → Finished goods
```

### 5.2 Key Components

**Modified:**
- `SubmitManufactureAsync()` implementation (replace internal logic)
- Manufacturing module dependencies (inject new FlexiBee clients)

**New Dependencies:**
- `IStockMovementClient` (from `Rem.FlexiBee` latest)
- `IStockItemsMovementClient` (from `Rem.FlexiBee` latest)

**Unchanged:**
- Controller layer (`ManufacturingController`)
- Request/Response DTOs
- MediatR handlers (if any)
- UI components

### 5.3 Data Flow

**Input:**
- Manufacturing request (materials, quantities, batch info)

**Processing:**
1. Validate material availability (existing logic)
2. Create consumption stock movement (materials OUT)
   - If fails → return error, stop
3. Create production stock movement (finished goods IN)
   - If fails → return error, log partial state

**Output:**
- Success: Both movement IDs returned
- Failure: Error details + any successful movement IDs for rollback

### 5.4 Error Scenarios

| Scenario | Handling |
|----------|----------|
| Consumption movement fails | Return error immediately, no production movement attempted |
| Production movement fails | Log consumption movement ID for manual rollback |
| FlexiBee API unavailable | Return error, operator retries manually |
| Invalid material quantities | Fail validation before any movements |

---

## 6. User Stories

### US-1: Successful Manufacturing Operation
**As a** manufacturing operator  
**I want to** submit a manufacturing order  
**So that** raw materials are consumed and finished goods are produced

**Acceptance Criteria:**
- Given valid materials and quantities
- When I submit manufacturing order
- Then consumption movement is created
- And production movement is created
- And both movements are linked to the operation

---

### US-2: Material Shortage Handling
**As a** manufacturing operator  
**I want to** see clear error when materials are insufficient  
**So that** I can restock before attempting manufacture

**Acceptance Criteria:**
- Given insufficient raw materials
- When I submit manufacturing order
- Then validation error is returned
- And no stock movements are created

---

### US-3: Partial Failure Recovery
**As a** system administrator  
**I want to** see which movements succeeded when production fails  
**So that** I can manually rollback or fix the issue

**Acceptance Criteria:**
- Given consumption movement succeeded
- When production movement fails
- Then error message includes consumption movement ID
- And I can manually reverse the consumption in FlexiBee

---

## 7. Testing Strategy

### 7.1 Unit Tests
**Scope:** New `SubmitManufactureAsync` implementation

**Test Cases:**
1. ✅ Successful creation of both movements
2. ✅ Consumption movement failure (no production attempted)
3. ✅ Production movement failure (consumption ID logged)
4. ✅ Material validation failures
5. ✅ Batch number preservation
6. ✅ Movement linkage verification

**Mocking:**
- Mock `IStockMovementClient`
- Mock `IStockItemsMovementClient`
- Mock material repository

### 7.2 Manual Testing
**Scenarios:**
1. Submit manufacturing with valid materials
2. Submit with insufficient materials
3. Verify historical orders still readable
4. Test error messages clarity

### 7.3 Out of Scope
- ❌ Integration tests (not required for MVP)
- ❌ E2E Playwright tests (backend change only)

---

## 8. Dependencies & Risks

### 8.1 Dependencies

| Dependency | Type | Impact |
|------------|------|--------|
| `Rem.FlexiBee` latest version | External NuGet | Critical - must upgrade |
| FlexiBee staging environment | External API | High - needed for validation |
| FlexiBee API documentation | External docs | Medium - understand new clients |

### 8.2 Risks

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Breaking changes in `Rem.FlexiBee` | Medium | High | Review changelog before upgrade |
| Atomicity issues cause data inconsistency | High | High | Clear error messages for manual rollback |
| Performance degradation (2 API calls) | Low | Medium | Monitor production performance post-deploy |
| FlexiBee API rate limits | Low | Medium | Implement retry logic if needed (future) |

---

## 9. Implementation Plan

### Phase 1: Preparation (Estimated: 1-2 days)
1. ✅ Upgrade `Rem.FlexiBee` NuGet packages
2. ✅ Review `IStockMovementClient` and `IStockItemsMovementClient` APIs
3. ✅ Identify breaking changes from package upgrade
4. ✅ Update dependency injection configuration

### Phase 2: Core Implementation (Estimated: 2-3 days)
1. ✅ Refactor `SubmitManufactureAsync` method
2. ✅ Implement consumption movement creation
3. ✅ Implement production movement creation
4. ✅ Add error handling for partial failures
5. ✅ Preserve batch/lot tracking logic

### Phase 3: Testing & Validation (Estimated: 1-2 days)
1. ✅ Write unit tests for new implementation
2. ✅ Update existing unit tests
3. ✅ Manual testing against FlexiBee staging
4. ✅ Verify historical orders still accessible

### Phase 4: Deployment (Estimated: 1 day)
1. ✅ Code review
2. ✅ Deploy to staging
3. ✅ Smoke test in staging environment
4. ✅ Deploy to production with monitoring

---

## 10. Open Questions

1. **Q:** What happens if FlexiBee API is temporarily unavailable during manufacturing?  
   **A:** User sees error and retries manually (no auto-retry in MVP)

2. **Q:** Should we log partial success states for auditing?  
   **A:** Yes - log consumption movement ID when production fails

3. **Q:** Do we need a migration path for in-flight manufacturing orders?  
   **A:** No - historical orders remain unchanged, only new submissions use new flow

4. **Q:** What's the rollback procedure for operators?  
   **A:** Document manual FlexiBee steps to reverse consumption movements (to be created)

---

## 11. Success Metrics

### Functional Metrics
- ✅ 100% of manufacturing operations create two stock movements
- ✅ Zero UI regressions
- ✅ All existing validations pass

### Quality Metrics
- ✅ Unit test coverage ≥ existing coverage
- ✅ Zero breaking changes to API contracts
- ✅ Code passes `dotnet format` validation

### Operational Metrics
- ✅ Error rate ≤ current manufacturing error rate
- ✅ Clear error messages for all failure scenarios
- ✅ Historical orders remain queryable

---

## 12. Appendix

### A. Related Documentation
- `docs/📘 Architecture Documentation – MVP Work.md` - Manufacturing module definition
- `docs/architecture/filesystem.md` - Backend structure guidelines
- `Rem.FlexiBee` NuGet package documentation
- FlexiBee API reference for stock movements

### B. Glossary
- **Stock Movement:** FlexiBee entity representing material flow between locations
- **Consumption Movement:** Movement reducing raw material inventory
- **Production Movement:** Movement increasing finished goods inventory
- **Batch/Lot Number:** Tracking identifier for manufactured batches

### C. Approval
- **Product Owner:** [TBD]
- **Technical Lead:** [TBD]
- **QA Lead:** [TBD]

---

**Document Status:** Ready for Review  
**Next Steps:** Technical implementation planning, FlexiBee API investigation