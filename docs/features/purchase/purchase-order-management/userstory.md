# User Story: Purchase Order Management

## Overview
As a purchasing manager, I want to manage purchase orders digitally instead of using Excel, so that I can track material and goods orders efficiently with proper status tracking and searchability.

## Business Value
- Replace manual Excel-based purchase order tracking
- Centralized purchase order management
- Better visibility into order statuses
- Searchable history of purchases
- Foundation for future advanced features (supplier management, analytics)

## Acceptance Criteria

### 1. Purchase Order List View
- [ ] Display all purchase orders in a sortable, filterable table
- [ ] Show key information: Order Number, Supplier, Date, Status, Total Amount
- [ ] Enable search by order number, supplier name, or material name
- [ ] Filter by status (Draft, In Transit, Completed)
- [ ] Filter by date range
- [ ] Sort by any column
- [ ] Pagination for large datasets

### 2. Create Purchase Order
- [ ] Create new purchase order with:
  - [ ] Supplier selection (from existing suppliers)
  - [ ] Order date
  - [ ] Expected delivery date
  - [ ] Status (defaults to Draft)
  - [ ] Notes/comments field
- [ ] Add order line items:
  - [ ] Material/goods selection
  - [ ] Quantity
  - [ ] Unit price
  - [ ] Total line amount (auto-calculated)
- [ ] Calculate order total automatically
- [ ] Save as draft for later editing
- [ ] Validate required fields before saving

### 3. Purchase Order Detail View
- [ ] Display full order information
- [ ] Show all line items with details
- [ ] Display order timeline/history
- [ ] Show supplier contact information
- [ ] Display order status prominently
- [ ] Calculate and show order totals

### 4. Edit Purchase Order
- [ ] Edit orders in Draft status
- [ ] Update order details (date, supplier, notes)
- [ ] Add/remove/modify line items
- [ ] Recalculate totals automatically
- [ ] Prevent editing of completed orders
- [ ] Track who made changes and when

### 5. Order Status Management
- [ ] Three status types:
  - **Draft**: Order being prepared, fully editable
  - **In Transit**: Order placed with supplier, limited editing
  - **Completed**: Order received, read-only
- [ ] Status transition rules:
  - Draft → In Transit (when order is sent)
  - In Transit → Completed (when goods received)
  - No backward transitions allowed
- [ ] Log status changes with timestamp and user

## Technical Requirements

### Data Model
```
PurchaseOrder:
- Id (GUID)
- OrderNumber (string, unique, auto-generated)
- SupplierId (GUID, FK)
- OrderDate (DateTime)
- ExpectedDeliveryDate (DateTime?)
- Status (enum: Draft, InTransit, Completed)
- Notes (string?)
- TotalAmount (decimal, calculated)
- CreatedBy (string)
- CreatedAt (DateTime)
- UpdatedBy (string?)
- UpdatedAt (DateTime?)

PurchaseOrderLine:
- Id (GUID)
- PurchaseOrderId (GUID, FK)
- MaterialId (GUID, FK)
- Quantity (decimal)
- UnitPrice (decimal)
- LineTotal (decimal, calculated)
- Notes (string?)

PurchaseOrderHistory:
- Id (GUID)
- PurchaseOrderId (GUID, FK)
- Action (string)
- OldValue (string?)
- NewValue (string?)
- ChangedBy (string)
- ChangedAt (DateTime)
```

### API Endpoints
- `GET /api/purchase-orders` - List with filtering/sorting
- `POST /api/purchase-orders` - Create new order
- `GET /api/purchase-orders/{id}` - Get order details
- `PUT /api/purchase-orders/{id}` - Update order
- `PUT /api/purchase-orders/{id}/status` - Change order status
- `GET /api/purchase-orders/{id}/history` - Get order history

### Frontend Components
- `PurchaseOrderList` - Main list view with search/filter
- `PurchaseOrderForm` - Create/edit form
- `PurchaseOrderDetail` - Detailed view
- `PurchaseOrderLineItem` - Line item component
- `PurchaseOrderStatusBadge` - Status display component

## Out of Scope (Future Stories)
- Supplier management (adding/editing suppliers)
- Purchase order approval workflow
- PDF generation/printing
- Email notifications
- Integration with ABRA Flexi
- Automatic material shortage detection
- Supplier performance analytics
- Multi-currency support
- Purchase order templates
- Bulk operations

## Dependencies
- Material/Goods catalog must be available
- Basic supplier data must exist (even if read-only)
- Authentication/authorization system

## Mockups
[To be added - basic wireframes for list view, create/edit form, and detail view]

## Definition of Done
- [ ] All acceptance criteria met
- [ ] Unit tests written (>80% coverage)
- [ ] Integration tests for API endpoints
- [ ] UI tests for critical workflows
- [ ] Code reviewed and approved
- [ ] Documentation updated
- [ ] Deployed to test environment
- [ ] Manual testing completed
- [ ] Performance acceptable (<2s load time)