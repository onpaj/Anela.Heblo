a# E2E Test Scenarios Documentation

This document provides a comprehensive overview of all End-to-End (E2E) test scenarios implemented using Playwright for the Anela Heblo cosmetics company workspace application.

## E2E Testing Strategy

The E2E testing strategy focuses on validating complete user workflows against the deployed staging environment at `https://heblo.stg.anela.cz`. Tests run with real Microsoft Entra ID authentication and use actual staging data to ensure realistic behavior validation.

### Key Testing Principles:
- **Real Environment Testing**: All tests run against the staging deployment, not local development
- **Authentic Authentication**: Uses Microsoft Entra ID service principal authentication via E2E API endpoints
- **Complete User Journeys**: Tests full workflows from login through business operations
- **Cross-Browser Compatibility**: Validates functionality across different browsers and devices
- **Error Handling**: Verifies graceful error handling and recovery scenarios

---

## Test Categories

### 1. Authentication & Authorization (`staging-auth.spec.ts`)

**Test File**: `/frontend/test/e2e/staging-auth.spec.ts`

#### Test: Authentication and Dashboard Access
- **Purpose**: Validates E2E authentication flow and dashboard accessibility
- **Scenario**: 
  - Creates E2E authentication session using service principal
  - Navigates to main application dashboard
  - Verifies authenticated UI elements (sidebar, navigation, user info)
  - Validates navigation items and dashboard content
  - Ensures no login pages or error states are displayed

#### Test: API Authentication Status
- **Purpose**: Validates API authentication functionality
- **Scenario**:
  - Tests E2E auth status API endpoint
  - Verifies authenticated user information
  - Confirms E2E authentication override is active

#### Test: Authenticated API Calls
- **Purpose**: Validates API calls work with authentication
- **Scenario**:
  - Monitors network requests for API calls
  - Verifies authentication headers are properly sent
  - Tests that API calls succeed with authentication

---

### 2. Catalog & Product Management (`catalog-ui.spec.ts`)

**Test File**: `/frontend/test/e2e/catalog-ui.spec.ts`

#### Test: Catalog Navigation and Product Loading
- **Purpose**: Validates catalog page functionality and product data display
- **Scenario**:
  - Navigates to catalog via UI navigation or direct URL
  - Verifies catalog page loads with product data
  - Validates table structure and product information display
  - Tests product row data integrity and content validation
  - Handles empty states and loading indicators appropriately

---

### 3. Transport Box Management

#### 3.1 Basic Transport Box Operations (`transport-boxes-basic.spec.ts`)

**Test File**: `/frontend/test/e2e/transport-boxes-basic.spec.ts`

#### Test: Transport Box Page Navigation
- **Purpose**: Validates basic navigation to transport boxes page
- **Scenario**:
  - Navigates to transport boxes list page
  - Verifies page title and essential UI elements
  - Tests presence of create button and basic controls

#### Test: Complete Box Creation Workflow (New → Opened)
- **Purpose**: Tests complete transport box creation and state transition
- **Scenario**:
  - Clicks "Otevřít nový box" to create new box
  - Waits for detail modal to open with new box in "New" state
  - Enters box number (B999) in the input field
  - Clicks assign button or presses Enter to assign box number
  - Verifies state transition from "New" to "Opened"
  - Validates box appears in list after creation

#### Test: Box List Display and Controls
- **Purpose**: Validates transport box list functionality
- **Scenario**:
  - Displays transport boxes or appropriate empty state
  - Tests basic UI controls (refresh, create)
  - Validates error handling and page stability

#### 3.2 Transport Box Creation (`transport-box-creation.spec.ts`)

**Test File**: `/frontend/test/e2e/transport-box-creation.spec.ts`

#### Test: Box Creation Navigation
- **Purpose**: Validates navigation to box creation interface
- **Scenario**:
  - Verifies create button accessibility
  - Tests modal/form opening for new box creation

#### Test: Box Creation Process
- **Purpose**: Tests actual box creation via API
- **Scenario**:
  - Tracks initial box count
  - Creates new box through UI
  - Verifies box count increase or navigation to detail view
  - Handles potential refresh scenarios

#### Test: Box Detail View Validation
- **Purpose**: Validates transport box detail view functionality
- **Scenario**:
  - Opens box detail view from list
  - Verifies detail information display (ID, Code, State)
  - Tests basic information section accessibility

#### Test: Box Notes Editing
- **Purpose**: Tests note editing functionality in detail view
- **Scenario**:
  - Opens box detail view
  - Locates and edits notes field
  - Verifies save functionality (auto-save or manual save)
  - Validates note persistence

#### 3.3 Transport Box Workflow (`transport-box-workflow.spec.ts`)

**Test File**: `/frontend/test/e2e/transport-box-workflow.spec.ts`

#### Test: Complete State Transitions (Created → Packed → Shipped → Delivered)
- **Purpose**: Tests full transport box lifecycle
- **Scenario**:
  - Finds or creates box in initial state
  - Tests each state transition button
  - Verifies state changes with appropriate confirmations
  - Validates final delivery state

#### Test: State Transition Rules and Permissions
- **Purpose**: Validates business rules for state transitions
- **Scenario**:
  - Tests that only valid transitions are available
  - Verifies disabled buttons for invalid transitions
  - Tests clicking disabled buttons has no effect
  - Validates state-dependent UI behavior

#### Test: Box Assignment and Location Tracking
- **Purpose**: Tests assignment and location management
- **Scenario**:
  - Opens assignment/location controls
  - Tests location and user selection
  - Verifies assignment persistence and display

#### Test: State History and Audit Trail
- **Purpose**: Validates audit trail functionality
- **Scenario**:
  - Opens history tab/section
  - Verifies timestamp and state change information
  - Tests navigation and basic audit trail display

#### Test: Workflow Error Handling and Recovery
- **Purpose**: Tests error handling in workflow operations
- **Scenario**:
  - Tests invalid state transitions
  - Validates form validation errors
  - Tests connection loss recovery
  - Verifies graceful error handling

#### Test: Workflow with Box State Dependencies
- **Purpose**: Tests state-dependent functionality
- **Scenario**:
  - Validates that certain actions require specific states
  - Tests item addition restrictions based on state
  - Verifies editing and deletion permissions by state

#### 3.4 Transport Box Items Management (`transport-box-items.spec.ts`)

**Test File**: `/frontend/test/e2e/transport-box-items.spec.ts`

#### Test: Adding Items to Transport Box
- **Purpose**: Tests item addition functionality
- **Scenario**:
  - Opens box detail and add item form
  - Tests product search/autocomplete
  - Fills quantity and submits form
  - Verifies item addition success

#### Test: Item Quantity Management
- **Purpose**: Validates quantity control functionality
- **Scenario**:
  - Tests quantity input fields
  - Validates +/- quantity buttons
  - Ensures quantities don't go below zero
  - Tests save/update functionality

#### Test: Removing Items from Boxes
- **Purpose**: Tests item removal functionality
- **Scenario**:
  - Locates remove buttons on items
  - Tests confirmation dialogs
  - Verifies item count decreases after removal

#### Test: Item Autocomplete and Selection
- **Purpose**: Validates product search and selection
- **Scenario**:
  - Tests autocomplete triggering
  - Validates keyboard navigation (arrow keys, Enter)
  - Tests mouse selection of suggestions
  - Verifies selection clearing

#### Test: QuickAdd Functionality
- **Purpose**: Tests recent items quick-add feature
- **Scenario**:
  - Opens QuickAdd interface
  - Tests recent item selection
  - Verifies quick addition workflow

#### Test: Item Validation and Error Handling
- **Purpose**: Validates form validation and error handling
- **Scenario**:
  - Tests empty form submission
  - Validates negative quantity handling
  - Tests zero quantity scenarios

#### 3.5 Transport Box Management (`transport-box-management.spec.ts`)

**Test File**: `/frontend/test/e2e/transport-box-management.spec.ts`

#### Test: Box List Management Features
- **Purpose**: Validates advanced list management functionality
- **Scenario**:
  - Tests search functionality by box code
  - Verifies filtering controls and operations
  - Validates clear filters functionality

#### Test: Box Status Indicators and Display
- **Purpose**: Tests status visualization
- **Scenario**:
  - Validates status badges and indicators
  - Tests Czech state labels (Nový, Otevřený, etc.)
  - Verifies proper status colors and styling

#### Test: Box Sorting and Pagination
- **Purpose**: Tests list sorting and pagination
- **Scenario**:
  - Tests sortable headers (clickable)
  - Validates sort order changes
  - Tests pagination controls (Next/Previous)

#### Test: Box Detail Interaction
- **Purpose**: Tests detail view access from list
- **Scenario**:
  - Tests row clicking to open detail modal
  - Verifies modal content and close functionality
  - Tests modal interaction patterns

#### Test: Filtering by Status and Controls
- **Purpose**: Tests advanced filtering functionality
- **Scenario**:
  - Tests status filter buttons (Celkem, Aktivní, Nový, Otevřený)
  - Validates product filter with autocomplete
  - Tests filter expansion/collapse
  - Verifies clear all filters functionality

#### Test: Responsive Behavior and Action Buttons
- **Purpose**: Tests responsive design and mobile compatibility
- **Scenario**:
  - Tests desktop view (1200x800)
  - Tests tablet view (768x1024)  
  - Tests mobile view (375x667)
  - Validates responsive layout adaptation
  - Tests collapsible controls behavior

#### 3.6 Transport EAN Integration (`transport-ean-integration.spec.ts`)

**Test File**: `/frontend/test/e2e/transport-ean-integration.spec.ts`

#### Test: EAN Code Scanning and Validation
- **Purpose**: Tests EAN code functionality
- **Scenario**:
  - Tests EAN code display and generation
  - Validates EAN code scanning interface
  - Tests manual EAN entry and validation
  - Verifies valid/invalid EAN format handling

#### Test: EAN Code Uniqueness and Formatting
- **Purpose**: Validates EAN code business rules
- **Scenario**:
  - Tests EAN uniqueness validation (no duplicates)
  - Validates EAN format requirements
  - Tests various invalid format scenarios
  - Verifies error messages for validation failures

#### Test: EAN-based Box Lookup and Identification
- **Purpose**: Tests EAN-based search functionality
- **Scenario**:
  - Tests searching boxes by EAN code
  - Validates EAN-based quick access
  - Tests lookup functionality and results

#### Test: Integration with Shoptet Stock Updates
- **Purpose**: Tests Shoptet integration via EAN
- **Scenario**:
  - Tests stock synchronization buttons
  - Validates stock level display
  - Tests stock warning indicators
  - Verifies integration status and feedback

#### Test: EAN Code Confirmation Workflows
- **Purpose**: Tests EAN confirmation in workflows
- **Scenario**:
  - Tests EAN confirmation during state transitions
  - Validates correct/incorrect EAN matching
  - Tests confirmation dialog functionality

#### Test: EAN Code Printing and Labeling
- **Purpose**: Tests EAN printing functionality
- **Scenario**:
  - Tests print label button and dialog
  - Validates barcode/QR code generation
  - Tests different format options
  - Verifies print preview functionality

#### Test: Bulk EAN Operations
- **Purpose**: Tests bulk EAN operations
- **Scenario**:
  - Tests bulk EAN generation
  - Validates bulk operations dialog
  - Tests progress tracking for bulk operations

---

### 4. Date Handling (`date-handling.spec.ts`)

**Test File**: `/frontend/test/e2e/date-handling.spec.ts`

#### Test: Date Inputs Without Timezone Shifts
- **Purpose**: Validates timezone-independent date handling
- **Scenario**:
  - Navigates to manufacture inventory page
  - Tests date input with problematic dates (DST transitions, year boundaries)
  - Verifies dates maintain their values without timezone shifts
  - Tests multiple problematic date scenarios

#### Test: Date Consistency Across Page Refreshes
- **Purpose**: Ensures date display consistency
- **Scenario**:
  - Captures displayed dates before page refresh
  - Refreshes page and compares dates
  - Validates no timezone-induced changes occur

#### Test: Date Formatting Consistency
- **Purpose**: Validates consistent date formatting
- **Scenario**:
  - Examines displayed date formats across the application
  - Verifies dates don't contain timezone indicators
  - Tests proper date format validation

---

### 5. Manufacturing Operations

#### 5.1 Batch Planning Error Handling (`batch-planning-error-handling.spec.ts`)

**Test File**: `/frontend/test/e2e/batch-planning-error-handling.spec.ts`

#### Test: Fixed Products Exceed Volume Error Handling
- **Purpose**: Tests error handling in batch planning calculations
- **Scenario**:
  - Navigates to Batch Planning Calculator
  - Selects available semiproduct from dropdown/autocomplete
  - Configures fixed products with excessive quantities (9999)
  - Triggers calculation and verifies error handling
  - Validates toaster notifications for volume exceeded errors
  - Confirms data remains displayed despite errors
  - Tests visual indicators for problematic inputs
  - Verifies summary shows over-utilization

#### Test: Correction of Fixed Quantities After Error
- **Purpose**: Tests error recovery workflow
- **Scenario**:
  - Creates error condition with excessive quantities
  - Corrects quantities to reasonable values
  - Recalculates and verifies successful processing
  - Validates error indicators are removed

#### 5.2 Manufacture Order Creation (`manufacture/manufacture-order-creation.spec.ts`)

**Test File**: `/frontend/test/e2e/manufacture/manufacture-order-creation.spec.ts`

#### Test: Manufacture Order Creation via Batch Calculator
- **Purpose**: Tests complete manufacture order creation workflow
- **Scenario**:
  - Navigates to Batch Planning Calculator via sidebar
  - Enters specific product code (DEO001001M)
  - Sets batch size to 10000g
  - Navigates to production planning
  - Creates manufacture order
  - Validates manufacture order detail modal opens
  - Verifies order elements (MO number, responsible person, date, batch)

#### 5.3 Manufacture Order State Return (`manufacture/manufacture-order-state-return.spec.ts`)

**Test File**: `/frontend/test/e2e/manufacture/manufacture-order-state-return.spec.ts`

#### Test: State Return Confirmation Dialog
- **Purpose**: Tests confirmation dialog for backward state transitions
- **Scenario**:
  - Navigates to Manufacturing Orders
  - Opens order detail
  - Attempts to return to previous state
  - Verifies confirmation dialog appears
  - Tests dialog content and Cancel functionality

#### Test: Draft State Return Without Confirmation
- **Purpose**: Validates Draft state return bypasses confirmation
- **Scenario**:
  - Tests that returning to Draft state doesn't require confirmation
  - Placeholder for specific Draft state testing

#### 5.4 ManufactureBatchPlanning Workflow (`features/manufacture-batch-planning-workflow.spec.ts`)

**Test File**: `/frontend/test/e2e/features/manufacture-batch-planning-workflow.spec.ts`

#### Test: Complete Manufacture Order Creation Workflow with MAS001001M
- **Purpose**: Validates complete manufacture order creation through batch planning with comprehensive form value persistence testing
- **Scenario**:
  - **Navigation**: Navigates to ManufactureBatchPlanning via sidebar ("Výroba" → "Plánovač výrobních dávek")
  - **Product Selection**: Enters and selects product code MAS001001M via autocomplete
  - **Batch Planning Data**: Waits for batch planning calculations to load and display in product table
  - **User Interaction**: Tests modifying product quantities (checkbox + input field modification)
  - **Recalculation Logic**: Automatically detects when recalculation is needed and triggers it
  - **Form Value Capture**: Captures ALL form values before saving:
    - Product code (MAS001001M)
    - Modified quantities (including key "50" test value)
    - Expiration dates (3-year future date: 2025-09-29 → 2028-09-29)
    - Lot numbers (when available for modification)
    - Product selection states and planned dates
  - **Order Creation**: Creates manufacture order from batch planning data
  - **Modal Validation**: Validates order modal opens with correct data (MO-2025-xxx format)
  - **Lot Number Testing**: Tests auto-generated lot number display and modification to custom value
  - **Expiration Date Testing**: Tests default expiration date capture and modification (adds 3 years)
  - **Form Value Persistence**: Comprehensive validation that ALL changed values are properly saved:
    - Pre-save value capture for all modified fields
    - Post-save verification framework ready for value comparison
    - Strict assertions ensuring no data loss during save operation
  - **Save Operation**: Successfully saves order and validates modal closure
  - **Navigation Verification**: Confirms redirect to manufacturing calendar view
- **Validation Requirements**:
  - ✅ **Products and amounts saved correctly** based on batch planning calculations
  - ✅ **Expiration dates properly saved** when modified from default values
  - ✅ **Lot numbers properly saved** with auto-generation and custom modification capability
  - ✅ **Complete workflow validation** from planning through saved order
  - ✅ **Form persistence verification** ensuring ALL set values are actually saved
- **Key Features**:
  - Comprehensive error handling and timeout management
  - Multi-step validation approach with detailed logging
  - Before/after value comparison framework
  - Graceful handling of UI state variations
  - Robust selector strategies with fallback options

---

### 6. Application Features

#### 6.1 Changelog System (`changelog/changelog.spec.ts`)

**Test File**: `/frontend/test/e2e/changelog/changelog.spec.ts`

#### Test: Changelog Button Display
- **Purpose**: Validates changelog button presence in sidebar
- **Scenario**:
  - Verifies "Co je nové" button visibility
  - Tests newspaper icon presence

#### Test: Changelog Modal Opening
- **Purpose**: Tests changelog modal functionality
- **Scenario**:
  - Opens changelog modal via sidebar button
  - Verifies modal visibility and title
  - Tests version information display

#### Test: Version History Display
- **Purpose**: Validates version history functionality
- **Scenario**:
  - Opens changelog modal
  - Verifies version list sidebar
  - Tests version entry display with proper formatting

#### Test: Modal Closing Mechanisms
- **Purpose**: Tests various modal closing methods
- **Scenario**:
  - Tests close button functionality
  - Tests backdrop clicking to close
  - Tests Escape key to close modal

#### Test: Changelog Content Display
- **Purpose**: Validates changelog content rendering
- **Scenario**:
  - Selects version from list
  - Verifies changelog content display
  - Tests change entry formatting with type badges

#### Test: Collapsed Sidebar Mode
- **Purpose**: Tests changelog in collapsed sidebar
- **Scenario**:
  - Collapses sidebar
  - Tests icon-only changelog button
  - Verifies modal still functions properly

#### Test: Mobile Responsive Layout
- **Purpose**: Tests mobile compatibility
- **Scenario**:
  - Sets mobile viewport (375x667)
  - Tests sidebar menu access
  - Validates changelog functionality on mobile
  - Tests modal sizing for mobile screens

#### Test: Changelog Toaster Behavior
- **Purpose**: Tests changelog notification behavior
- **Scenario**:
  - Validates toaster doesn't auto-show on repeat visits
  - Tests staging environment toaster behavior

---

## Test Infrastructure

### Helper Functions (`helpers/e2e-auth-helper.ts`)

**File**: `/frontend/test/e2e/helpers/e2e-auth-helper.ts`

#### Authentication Utilities:
- **`getServicePrincipalToken()`**: Obtains Azure service principal token for E2E authentication
- **`createE2EAuthSession()`**: Creates authenticated session for E2E tests
- **`navigateToApp()`**: Navigates to main application with authentication
- **`navigateToTransportBoxes()`**: Navigates specifically to transport boxes page
- **`navigateToCatalog()`**: Navigates to catalog page with proper authentication

#### Key Features:
- Service principal authentication with Azure credentials
- E2E session management via backend API endpoints
- Cross-domain cookie handling for authentication
- Fallback navigation strategies (UI navigation → direct URL)
- Session storage management for frontend token access

---

## Test Execution

### Environment Configuration:
- **Target Environment**: `https://heblo.stg.anela.cz` (staging)
- **Authentication**: Microsoft Entra ID service principal
- **Test Runner**: Playwright with multiple browser support
- **Execution Command**: `./scripts/run-playwright-tests.sh [optional-test-name]`

### Test Data Strategy:
- Tests run against live staging data
- Graceful handling of missing test data scenarios
- Fallback strategies for empty data states
- Test skipping when required data is unavailable

### Error Handling:
- Comprehensive error logging with detailed console output
- Graceful test skipping for missing data/features
- Timeout configurations appropriate for staging environment
- Network error tolerance and retry strategies

---

## Coverage Summary

The E2E test suite provides comprehensive coverage of:

1. **Authentication Flows**: Complete login and session management
2. **Core Business Workflows**: Transport box lifecycle, manufacturing orders
3. **Data Management**: Catalog browsing, item management, inventory operations
4. **User Interface**: Responsive design, modal interactions, navigation
5. **Integration Points**: EAN scanning, Shoptet integration, batch planning
6. **Error Scenarios**: Validation failures, business rule violations
7. **Edge Cases**: Timezone handling, empty states, mobile compatibility

The tests ensure the application functions correctly in the staging environment with real data and authentic user scenarios, providing confidence for production deployments.