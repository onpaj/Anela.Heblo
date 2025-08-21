# Journal Feature - User Story

## Overview

The Journal feature enables users to create, manage, and search through date-based and product-related notes throughout the application. This feature provides a centralized logging system that can be linked to specific dates, products, or product families, with integration points across multiple application areas.

## Feature Vision

Create a comprehensive journaling system that allows users to:
- Record timestamped notes with flexible associations (dates, products, product families)
- Search and filter journal entries with full-text search capabilities
- Access relevant journal entries contextually within other application modules
- Maintain a centralized audit trail of important events and observations

## Three-Phase Implementation Plan

### Phase 1: Standalone Journal Management
**Goal**: Create independent journal functionality with full CRUD operations and search capabilities

**Features**:
- Standalone Journal page with complete entry management
- Add/edit/delete journal entries
- Full-text search across all journal entries
- Date-based filtering and sorting
- Product association (single and multiple products)
- Product family association (ProductCode prefix matching)

### Phase 2: Catalog Integration
**Goal**: Display relevant journal entries within product detail views

**Features**:
- Show journal entries on CatalogDetail page for specific product
- Display entries linked to product family (ProductCode prefix)
- Quick add journal entry directly from CatalogDetail
- Visual indicators for products with associated journal entries

### Phase 3: Cross-Module Integration
**Goal**: Provide journal entry access and indicators across all relevant application modules

**Features**:
- Journal entry indicators in product lists and grids
- Quick access to relevant entries from various modules
- Contextual journal entry creation from different application areas
- Dashboard widgets showing recent journal activity

## User Stories

### Phase 1: Standalone Journal Management

#### US-J1.1: View Journal List
**As a** user  
**I want to** see a list of all journal entries  
**So that** I can browse through my recorded notes and observations

**Acceptance Criteria**:
- Display journal entries in reverse chronological order (newest first)
- Show entry date, title/preview text, and associated products
- Support pagination for large numbers of entries
- Display product associations as clickable tags
- Show entry creation and last modification timestamps

#### US-J1.2: Create New Journal Entry
**As a** user  
**I want to** create a new journal entry  
**So that** I can record important information, observations, or notes

**Acceptance Criteria**:
- Entry form with date picker (defaults to current date)
- Rich text editor for content
- Optional title field
- Product association selector (search and multi-select)
- Product family association (ProductCode prefix input)
- Category/tag selection for organizing entries
- Save and continue editing option
- Validation for required fields

#### US-J1.3: Edit Journal Entry
**As a** user  
**I want to** edit existing journal entries  
**So that** I can update or correct information

**Acceptance Criteria**:
- Edit all fields from creation form
- Show last modified timestamp and user
- Preserve original creation date
- Confirm before saving changes

#### US-J1.4: Delete Journal Entry
**As a** user  
**I want to** delete journal entries  
**So that** I can remove outdated or incorrect information

**Acceptance Criteria**:
- Confirmation dialog before deletion

#### US-J1.5: Search Journal Entries
**As a** user  
**I want to** search through journal entries  
**So that** I can quickly find specific information

**Acceptance Criteria**:
- Full-text search across titles and content
- Filter by date range
- Filter by associated products
- Filter by product family (ProductCode prefix)
- Filter by categories/tags
- Sort by relevance, date, or modification time
- Highlight search terms in results

### Phase 2: Catalog Integration

#### US-J2.1: View Product-Related Entries in Catalog Detail
**As a** user  
**I want to** see relevant journal entries on product detail pages  
**So that** I can access product-specific notes while viewing product information

**Acceptance Criteria**:
- Display entries directly associated with the product or product family in a Journal tab
- Sort by relevance and date
- Limit to most recent/relevant entries with "view all" option

#### US-J2.2: Quick Add Journal Entry from Catalog Detail
**As a** user  
**I want to** quickly create journal entries while viewing product details  
**So that** I can immediately record observations or notes

**Acceptance Criteria**:
- "Add Journal Entry" button on CatalogDetail page
- Pre-populate with current product association
- Pre-fill date with current date
- Streamlined form with essential fields only
- Option to expand to full entry form
- Save and stay on current page option

### Phase 3: Cross-Module Integration

#### US-J3.1: Journal Integration in Product Lists
**As a** user  
**I want to** see journal indicators in product lists across the application  
**So that** I can identify products with documentation from any view

**Acceptance Criteria**:
- Journal indicators in CatalogList
- Journal indicators in Purchase analysis views
- Journal indicators in Manufacturing views
- Journal indicators in Margin views
- Consistent visual treatment across modules
- Quick preview on hover/click

## Data Model Specifications

### JournalEntry Entity
```csharp
public class JournalEntry
{
    public int Id { get; set; }
    public string? Title { get; set; }
    public string Content { get; set; }
    public DateTime EntryDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ModifiedAt { get; set; }
    public string CreatedByUserId { get; set; }
    public string? ModifiedByUserId { get; set; }
    
    // Product associations
    public List<JournalEntryProduct> AssociatedProducts { get; set; }
    public List<JournalEntryProductFamily> AssociatedProductFamilies { get; set; }
    
    // Categorization
    public List<JournalEntryTag> Tags { get; set; }
}

public class JournalEntryProduct
{
    public int JournalEntryId { get; set; }
    public string ProductCode { get; set; }
    // Navigation properties
}

public class JournalEntryProductFamily
{
    public int JournalEntryId { get; set; }
    public string ProductCodePrefix { get; set; }
    // Navigation properties
}

public class JournalEntryTag
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Color { get; set; } // For UI display
}
```

## API Endpoints Specification

### Phase 1 Endpoints
- `GET /api/journal` - List journal entries with filtering
- `POST /api/journal` - Create new entry
- `GET /api/journal/{id}` - Get specific entry
- `PUT /api/journal/{id}` - Update entry
- `DELETE /api/journal/{id}` - Soft delete entry
- `GET /api/journal/search` - Full-text search with filters
- `GET /api/journal/tags` - Get available tags
- `POST /api/journal/tags` - Create new tag

### Phase 2 Endpoints
- `GET /api/journal/by-product/{productCode}` - Get entries for specific product
- `GET /api/journal/by-product-family/{prefix}` - Get entries for product family

### Phase 3 Endpoints
- `GET /api/journal/indicators` - Get journal indicators for product lists
- `GET /api/journal/dashboard-summary` - Get dashboard widget data

## UI/UX Specifications

### Phase 1: Journal Management Page
- **Layout**: Standard page with sidebar navigation
- **List View**: Table/card layout with search and filters in sidebar
- **Entry Form**: Modal or dedicated page with rich text editor
- **Search Interface**: Prominent search bar with advanced filter toggles
- **Responsive Design**: Mobile-friendly with collapsible filters

### Phase 2: Catalog Integration
- **Product Detail Section**: Dedicated "Journal" tab or collapsible section
- **Entry Previews**: Card layout with expand/collapse for full content
- **Quick Add Form**: Inline form that expands when needed
- **Visual Indicators**: Small, subtle icons that don't clutter the interface

### Phase 3: Cross-Module Integration
- **Indicators**: Consistent icon/badge system across all modules
- **Quick Actions**: Context menus or hover actions for journal operations
- **Dashboard Widget**: Clean, modern card design matching existing widgets

## Performance Considerations

- **Search Optimization**: Full-text search indexes on title and content
- **Product Association Queries**: Efficient queries for product family matching
- **Pagination**: Server-side pagination for large journal datasets
- **Caching**: Cache frequently accessed entries and search results
- **Lazy Loading**: Load journal sections only when expanded in other modules

## Security & Permissions

- **User-Level Security**: Users can only edit their own entries
- **Read Access**: All authenticated users can read all entries
- **Admin Privileges**: Admins can edit/delete any entry
- **Audit Trail**: Track all modifications with user and timestamp
- **Soft Deletion**: Preserve data integrity with recoverable deletes