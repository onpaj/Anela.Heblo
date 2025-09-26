# Feature: Drag & Drop for ManufactureOrders in Calendar View

## Overview
Implement drag and drop functionality in the weekly calendar view of ManufactureOrders, allowing users to move order cards between different days.

## User Story
As a production planner, I want to drag and drop manufacture order cards between days in the weekly calendar view so that I can easily reschedule production dates.

## Current State
- ManufactureOrders has a weekly calendar view
- Calendar displays individual cards with manufacture orders
- Orders are currently distributed by some date field (to be determined)

## Proposed Functionality

### Core Feature
- **Draggable Elements**: Individual manufacture order cards in calendar view
- **Drop Zones**: Calendar day containers (Monday through Sunday)
- **Action**: Moving an order card updates its scheduled/production date

### Technical Requirements

#### Frontend (React)
- Implement drag & drop using HTML5 Drag and Drop API or library (e.g., react-beautiful-dnd)
- Visual feedback during drag operations (hover states, drop zones highlighting)
- Success/error notifications after drop operations
- Responsive design - consider touch interactions for tablets

#### Backend (Clean Architecture)
- Update appropriate date field in ManufactureOrder entity when moved
- Validate business rules before allowing the move
- Create audit trail for schedule changes
- Follow existing Application/Features/ManufactureOrders pattern

### Business Rules (TBD)
The following need to be clarified during implementation:
- Which date field gets updated (ScheduledDate, ProductionDate, etc.)
- Validation rules (capacity limits, past dates, etc.)
- Integration with existing scheduling logic
- Conflict resolution for concurrent updates

### User Experience
- Smooth drag animations
- Clear visual indicators for valid/invalid drop zones
- Immediate feedback on successful moves
- Error handling for invalid operations

## Technical Implementation

### Architecture Alignment
- Follows Clean Architecture with Vertical Slice organization
- New MediatR handlers in `Application/Features/ManufactureOrders/UseCases/`
- Controller endpoints in `API/Controllers/ManufactureOrdersController.cs`
- Frontend integration via generated TypeScript client

### Suggested API Endpoints
```
PUT /api/manufactureorders/{id}/schedule
Body: { "newScheduledDate": "2024-01-15" }
```

## Acceptance Criteria
- [ ] User can drag manufacture order cards within weekly calendar view
- [ ] User can drop cards on different days to reschedule them
- [ ] Backend validates and persists the schedule changes
- [ ] UI provides immediate feedback on successful/failed operations
- [ ] Changes are reflected in real-time in the calendar view
- [ ] Responsive design works on desktop and tablet devices

## Technical Tasks
- [ ] Design and implement drag & drop UI components
- [ ] Create MediatR handler for schedule update operations
- [ ] Add API endpoint for updating manufacture order dates
- [ ] Implement validation logic for date changes
- [ ] Add frontend integration with TypeScript client
- [ ] Write unit tests for business logic
- [ ] Write E2E tests for drag & drop interactions
- [ ] Update documentation if needed

## Questions for Clarification
1. Which ManufactureOrder date field should be updated when moved?
2. What validation rules should apply (capacity, past dates, etc.)?
3. Should this create audit trail entries?
4. Are there any existing scheduling constraints to consider?
5. How should concurrent updates be handled?

## Priority
TBD - to be assigned based on product priorities

## Labels
- `feature`
- `frontend`
- `backend`
- `manufacture-orders`
- `calendar`
- `drag-drop`