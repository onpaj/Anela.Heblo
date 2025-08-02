# Test Scenarios: Purchase Order Management

## 1. Purchase Order Creation

### Scenario 1.1: Create Valid Purchase Order
**Given**: User is on the purchase order creation page
**When**: User fills in all required fields and adds at least one line item
**Then**: 
- Purchase order is created with status "Draft"
- Order number is auto-generated
- User is redirected to order detail page
- Success notification is displayed

### Scenario 1.2: Create Order Without Line Items
**Given**: User is on the purchase order creation page
**When**: User fills in header information but adds no line items
**Then**: 
- Validation error is displayed
- Order is not saved
- Error message: "Purchase order must contain at least one line item"

### Scenario 1.3: Create Order with Invalid Data
**Given**: User is on the purchase order creation page
**When**: User submits form with missing required fields
**Then**: 
- Validation errors are displayed for each missing field
- Form remains on the same page
- Previously entered data is retained

## 2. Purchase Order List and Search

### Scenario 2.1: View All Purchase Orders
**Given**: User navigates to purchase orders list
**When**: Page loads
**Then**: 
- All purchase orders are displayed in descending order by date
- Each row shows: Order Number, Supplier, Date, Status, Total
- Pagination controls are visible if more than 20 orders

### Scenario 2.2: Search by Order Number
**Given**: User is on purchase orders list
**When**: User enters partial order number in search field
**Then**: 
- List updates to show only matching orders
- Search is case-insensitive
- Results update as user types (with debounce)

### Scenario 2.3: Filter by Status
**Given**: User is on purchase orders list
**When**: User selects "In Transit" from status filter
**Then**: 
- Only orders with "In Transit" status are displayed
- Filter can be combined with search
- Clear filter option is available

### Scenario 2.4: Combined Search and Filter
**Given**: User is on purchase orders list
**When**: User searches for supplier name AND filters by "Draft" status
**Then**: 
- Only draft orders from matching supplier are shown
- Both filters remain active
- Results count is displayed

## 3. Purchase Order Editing

### Scenario 3.1: Edit Draft Order
**Given**: User is viewing a purchase order with "Draft" status
**When**: User clicks "Edit" button
**Then**: 
- Form opens in edit mode
- All fields are editable
- User can add/remove line items
- Save updates the order and recalculates totals

### Scenario 3.2: Attempt to Edit In Transit Order
**Given**: User is viewing a purchase order with "In Transit" status
**When**: User clicks "Edit" button
**Then**: 
- Only notes field is editable
- Line items cannot be modified
- Warning message: "Orders in transit have limited editing"

### Scenario 3.3: Attempt to Edit Completed Order
**Given**: User is viewing a purchase order with "Completed" status
**When**: User looks for edit button
**Then**: 
- No edit button is visible
- Order is read-only
- Status history shows completion details

## 4. Status Management

### Scenario 4.1: Change Status from Draft to In Transit
**Given**: User is viewing a draft purchase order
**When**: User clicks "Send to Supplier" button
**Then**: 
- Confirmation dialog appears
- After confirmation, status changes to "In Transit"
- Status change is logged with timestamp
- Email notification sent (if configured)

### Scenario 4.2: Mark Order as Completed
**Given**: User is viewing an "In Transit" order
**When**: User clicks "Mark as Received" button
**Then**: 
- Status changes to "Completed"
- User can add receiving notes
- Order becomes read-only
- Inventory updated (future feature)

### Scenario 4.3: Invalid Status Transition
**Given**: Order is in "Completed" status
**When**: System attempts any status change
**Then**: 
- Operation is rejected
- Error: "Completed orders cannot change status"

## 5. Line Item Management

### Scenario 5.1: Add Line Item
**Given**: User is creating/editing a draft order
**When**: User clicks "Add Line Item" and fills in details
**Then**: 
- New line appears in the order
- Line total is calculated (quantity × unit price)
- Order total is updated automatically

### Scenario 5.2: Remove Line Item
**Given**: Draft order has multiple line items
**When**: User clicks delete icon on a line item
**Then**: 
- Confirmation dialog appears
- Line is removed after confirmation
- Order total is recalculated

### Scenario 5.3: Update Line Item Quantity
**Given**: User is editing a line item
**When**: User changes quantity value
**Then**: 
- Line total updates automatically
- Order total updates automatically
- Changes are highlighted until saved

## 6. Data Validation

### Scenario 6.1: Negative Quantity
**Given**: User is entering line item details
**When**: User enters negative quantity
**Then**: 
- Validation error appears
- "Quantity must be greater than zero"
- Form cannot be submitted

### Scenario 6.2: Future Order Date
**Given**: User is creating an order
**When**: User selects order date in the future
**Then**: 
- Warning message appears
- "Order date is in the future. Continue?"
- User can proceed or correct

### Scenario 6.3: Duplicate Order Number
**Given**: System generates order number
**When**: Generated number already exists
**Then**: 
- System automatically generates new number
- No user intervention required
- Unique constraint is maintained

## 7. Performance and Edge Cases

### Scenario 7.1: Large Order with Many Line Items
**Given**: User creates order with 50+ line items
**When**: User saves the order
**Then**: 
- Save completes within 2 seconds
- All calculations are correct
- UI remains responsive

### Scenario 7.2: Concurrent Editing
**Given**: Two users open same draft order
**When**: Both attempt to save changes
**Then**: 
- First save succeeds
- Second save shows conflict error
- User can reload and retry

### Scenario 7.3: Network Failure During Save
**Given**: User is saving a purchase order
**When**: Network connection fails
**Then**: 
- Error message is displayed
- Form data is preserved
- Retry option is available
- Auto-save draft (if implemented)

## 8. Authorization and Security

### Scenario 8.1: Unauthorized Access
**Given**: User without purchase permissions
**When**: User tries to access purchase orders
**Then**: 
- Access denied message
- Redirect to dashboard
- Incident logged

### Scenario 8.2: View Own Orders Only
**Given**: User with limited permissions
**When**: User accesses purchase order list
**Then**: 
- Only orders created by user are shown
- No access to others' orders
- Filters work within allowed scope

## Test Data Requirements

### Suppliers
- At least 5 test suppliers with different names
- Mix of active and inactive suppliers

### Materials/Goods
- At least 20 test materials
- Various units of measure
- Different price ranges

### Purchase Orders
- At least 30 test orders
- Mix of all statuses
- Various dates (past 6 months)
- Different suppliers
- 1-20 line items per order

### Users
- Admin user (full access)
- Purchase manager (create/edit/view all)
- Limited user (view only own orders)
- No access user (for security testing)