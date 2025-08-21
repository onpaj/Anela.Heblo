# Journal Feature

## Overview

The Journal feature provides a comprehensive note-taking and documentation system that allows users to record timestamped observations, issues, and insights with flexible associations to products, product families, or specific dates. This feature serves as a centralized knowledge base that integrates across multiple application modules.

## Key Features

- **Flexible Associations**: Link entries to specific products, product families (via ProductCode prefix), or dates
- **Full-Text Search**: Advanced search capabilities across all entry content
- **Cross-Module Integration**: Access journal entries from catalog, purchase, and other modules
- **Rich Content Support**: HTML-formatted content with sanitization for security
- **Tagging System**: Organize entries with customizable tags and colors
- **Audit Trail**: Complete modification history with user tracking
- **Soft Delete**: Recoverable deletion with data preservation

## Documentation Structure

### [User Story](./userstory.md)
Comprehensive user stories covering all three implementation phases:
- **Phase 1**: Standalone journal management with CRUD operations and search
- **Phase 2**: Integration with catalog module for product-specific entries
- **Phase 3**: Cross-module indicators and contextual entry creation

### [Technical Specifications](./technical_specifications.md)
Complete technical implementation details including:
- Domain model design with entity relationships
- Repository patterns and database configurations
- MediatR handlers and API endpoints
- Performance optimizations and security considerations
- Frontend integration patterns

### [Test Scenarios](./test_scenarios.md)
Comprehensive testing strategy covering:
- Unit tests for business logic and data operations
- Integration tests for API endpoints and cross-module functionality
- UI/UX tests for user interface validation
- Performance and security testing scenarios

## Implementation Phases

### Phase 1: Core Journal Functionality
- Standalone journal page with full CRUD operations
- Advanced search with filtering by date, product, and content
- Product association management
- Tag system with color coding
- User access control and audit trails

### Phase 2: Catalog Integration
- Display relevant journal entries on product detail pages
- Quick entry creation from product views
- Visual indicators for products with associated entries
- Product family association via ProductCode prefixes

### Phase 3: Application-Wide Integration
- Journal indicators in product lists across all modules
- Contextual entry creation from various application areas
- Dashboard widgets showing recent journal activity
- Cross-module navigation to relevant entries

## Architecture Alignment

The Journal feature follows the established Vertical Slice Architecture:
- **Domain Layer**: Entities and business logic in `Anela.Heblo.Domain`
- **Application Layer**: MediatR handlers and interfaces in `Anela.Heblo.Application/Features/Journal`
- **API Layer**: RESTful endpoints in `Anela.Heblo.API/Controllers`
- **Frontend**: React components with TypeScript API client integration

## Database Schema

The feature introduces four main tables:
- `JournalEntries`: Core journal entry data with soft delete support
- `JournalEntryProducts`: Direct product associations
- `JournalEntryProductFamilies`: Product family associations via prefix matching
- `JournalEntryTags`: Tag definitions with color customization
- `JournalEntryTagAssignments`: Many-to-many tag assignments

## API Endpoints

RESTful API following the established `/api/{controller}` pattern:

- `GET /api/journal` - List and search journal entries
- `POST /api/journal` - Create new entry
- `GET /api/journal/{id}` - Get specific entry
- `PUT /api/journal/{id}` - Update entry
- `DELETE /api/journal/{id}` - Soft delete entry
- `GET /api/journal/by-product/{productCode}` - Get product-specific entries
- `POST /api/journal/indicators` - Get journal indicators for products
- `GET /api/journal/tags` - Manage tags

## Frontend Integration

- **React Components**: Journal management pages and integrated widgets
- **TypeScript Hooks**: `useJournalEntries`, `useCreateJournalEntry`, `useJournalIndicators`
- **Generated API Client**: Automatic TypeScript client via NSwag
- **Responsive Design**: Mobile-friendly with consistent UI patterns

## Security & Performance

- **Authentication**: All endpoints require valid authentication
- **Authorization**: Users can only edit their own entries
- **Input Sanitization**: HTML content sanitization for XSS protection
- **Performance**: Full-text search indexes and optimized queries
- **Audit Trail**: Complete modification tracking with user attribution

## Getting Started

1. **Review the user stories** to understand feature requirements and user workflows
2. **Study technical specifications** for implementation details and architecture decisions  
3. **Examine test scenarios** to understand validation requirements and testing strategy
4. **Follow the three-phase implementation plan** for systematic feature development

## Future Enhancements

- Email notifications for journal entries
- Export functionality (PDF, Excel)
- Advanced analytics and reporting
- Integration with external documentation systems
- Collaborative editing features
- File attachments and media support

---

This feature specification provides a complete foundation for implementing a robust, scalable journal system that integrates seamlessly with the existing application architecture.