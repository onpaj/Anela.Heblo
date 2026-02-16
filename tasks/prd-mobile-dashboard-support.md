# PRD: Mobile Dashboard Support

## Introduction

Enable smartphone users to access the Heblo Dashboard for quick monitoring and data viewing on-the-go. The dashboard will be optimized for portrait-oriented mobile screens (320px-428px wide), providing an easy-to-use experience for viewing dashboard tiles without requiring desktop functionality. Other application pages will remain accessible but with a degraded experience and user notification.

## Goals

- Enable smartphone users to view all dashboard tiles in a mobile-optimized layout
- Provide touch-friendly, read-only monitoring experience for quick data checks
- Maintain visual hierarchy and readability of tile information on small screens
- Ensure graceful degradation for non-dashboard pages accessed on mobile
- Preserve desktop experience without compromising mobile usability

## User Stories

### US-001: Mobile-responsive dashboard grid layout
**Description:** As a mobile user, I want the dashboard tiles to display in a single-column layout so that I can easily scroll through all information without horizontal scrolling.

**Acceptance Criteria:**
- [ ] Dashboard grid switches to single-column layout (`grid-cols-1`) on mobile screens (<768px)
- [ ] All tile sizes (Small, Medium, Large) render as full-width single tiles on mobile
- [ ] Tiles maintain their visual styling (shadows, borders, rounded corners)
- [ ] Vertical spacing between tiles is consistent and touch-friendly (min 16px gap)
- [ ] No horizontal scrolling occurs on any mobile device (320px-428px wide)
- [ ] Typecheck passes
- [ ] Verify in browser using dev-browser skill with mobile device emulation

### US-002: Disable drag-and-drop on mobile
**Description:** As a mobile user, I want tiles to remain static (non-draggable) so that I can scroll smoothly without accidentally triggering drag operations.

**Acceptance Criteria:**
- [ ] Drag-and-drop functionality (`@dnd-kit`) is disabled on mobile screens (<768px)
- [ ] Drag handle indicators are hidden on mobile
- [ ] Tiles do not respond to touch-and-hold drag gestures
- [ ] Scrolling is smooth and not interrupted by drag detection
- [ ] Desktop drag-and-drop functionality remains unchanged
- [ ] Typecheck passes
- [ ] Verify in browser using dev-browser skill with mobile device emulation

### US-003: Optimize tile content for mobile display
**Description:** As a mobile user, I want tile content to be readable and properly sized so that I can quickly understand the information without zooming.

**Acceptance Criteria:**
- [ ] Tile titles use mobile-appropriate font sizes (min 16px)
- [ ] Count tiles display numbers prominently (min 24px font size)
- [ ] Chart tiles (Production, Inventory Summary) render at appropriate height (min 200px, max 300px)
- [ ] Icons in Count tiles are properly sized (min 32px)
- [ ] Text content has adequate line-height for readability (min 1.5)
- [ ] All tile content fits within viewport width without clipping
- [ ] Typecheck passes
- [ ] Verify in browser using dev-browser skill with mobile device emulation

### US-004: Mobile-friendly tile navigation
**Description:** As a mobile user, I want clickable tiles to have touch-friendly tap targets so that I can easily navigate to detail pages.

**Acceptance Criteria:**
- [ ] All interactive tile areas have minimum tap target size of 44px height
- [ ] Tiles with `targetUrl` prop show visual touch feedback (active state)
- [ ] Touch feedback is immediate (no 300ms delay)
- [ ] Navigation occurs on `touchend` event for native feel
- [ ] Hover states are replaced with active states on mobile
- [ ] Typecheck passes
- [ ] Verify in browser using dev-browser skill with mobile device emulation

### US-005: Hide dashboard settings on mobile
**Description:** As a mobile user, I don't need tile customization features on mobile since this is a read-only monitoring experience.

**Acceptance Criteria:**
- [ ] Dashboard settings button is hidden on mobile screens (<768px)
- [ ] Settings panel cannot be accessed on mobile devices
- [ ] Tile visibility/ordering remains managed on desktop only
- [ ] Mobile users see all enabled tiles in their configured order
- [ ] Typecheck passes
- [ ] Verify in browser using dev-browser skill with mobile device emulation

### US-006: Optimize dashboard header for mobile
**Description:** As a mobile user, I want a compact dashboard header so that more screen space is available for tile content.

**Acceptance Criteria:**
- [ ] Dashboard title reduces to smaller size on mobile (text-2xl instead of text-3xl)
- [ ] Dashboard description text is hidden or abbreviated on mobile (<768px)
- [ ] Header padding is reduced on mobile (px-4 instead of px-6)
- [ ] Header remains sticky at top when scrolling (optional)
- [ ] Header height does not exceed 64px on mobile
- [ ] Typecheck passes
- [ ] Verify in browser using dev-browser skill with mobile device emulation

### US-007: Mobile viewport meta tag configuration
**Description:** As a mobile user, I want the application to render at the correct scale so that I don't see a zoomed-out desktop view.

**Acceptance Criteria:**
- [ ] Viewport meta tag includes `width=device-width, initial-scale=1.0`
- [ ] Viewport meta tag includes `maximum-scale=1.0` to prevent zoom on form inputs
- [ ] User-scalable is set appropriately for accessibility
- [ ] Application renders at 100% scale on all mobile devices
- [ ] Typecheck passes

### US-008: Non-dashboard page mobile notice
**Description:** As a mobile user accessing non-dashboard pages, I want to see a notice that the page is optimized for desktop so that I understand the degraded experience.

**Acceptance Criteria:**
- [ ] Mobile notice component created for non-dashboard pages
- [ ] Notice displays at top of page on mobile screens (<768px)
- [ ] Notice includes icon, text: "This page is optimized for desktop. Some features may not work properly on mobile."
- [ ] Notice is dismissible with close button (stored in sessionStorage)
- [ ] Notice does not display on dashboard page
- [ ] Notice has appropriate styling (warning yellow background, padding)
- [ ] Typecheck passes
- [ ] Verify in browser using dev-browser skill with mobile device emulation

### US-009: Mobile sidebar behavior
**Description:** As a mobile user, I want the sidebar to work properly on mobile so that I can navigate between pages easily.

**Acceptance Criteria:**
- [ ] Sidebar is hidden by default on mobile screens (<768px)
- [ ] Hamburger menu button is visible in mobile header/status bar
- [ ] Tapping hamburger opens sidebar as overlay (z-index > main content)
- [ ] Sidebar overlay includes backdrop (semi-transparent black background)
- [ ] Tapping backdrop or navigation item closes sidebar
- [ ] Sidebar animations are smooth on mobile devices
- [ ] Sidebar width is 80% of screen width on mobile (max 280px)
- [ ] Typecheck passes
- [ ] Verify in browser using dev-browser skill with mobile device emulation

### US-010: Test dashboard on real mobile devices
**Description:** As a developer, I want to verify the mobile dashboard works correctly on real devices so that I can catch device-specific issues.

**Acceptance Criteria:**
- [ ] Dashboard tested on iOS Safari (iPhone 12 or newer)
- [ ] Dashboard tested on Android Chrome (recent Android version)
- [ ] All tiles render correctly on both platforms
- [ ] Touch interactions work smoothly (no delays, proper feedback)
- [ ] Performance is acceptable (no lag when scrolling)
- [ ] No console errors on mobile browsers
- [ ] Screenshots documented in PR for verification

## Functional Requirements

**FR-1: Mobile Breakpoint Detection**
The system must detect mobile devices using CSS media queries with breakpoint `<768px` for mobile-specific styling.

**FR-2: Responsive Grid System**
The dashboard grid must switch from multi-column layout (`grid-cols-1 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-6`) to single-column (`grid-cols-1`) on mobile screens.

**FR-3: Touch-Optimized Interactions**
All interactive elements must have minimum 44px tap targets and provide immediate visual feedback on touch.

**FR-4: Tile Content Scaling**
All tile types must render their content appropriately for mobile screen widths (320px-428px) without clipping or overflow.

**FR-5: Disabled Drag-and-Drop**
The drag-and-drop tile reordering functionality must be completely disabled on mobile devices to prevent interference with scrolling.

**FR-6: Settings Panel Restriction**
The dashboard settings panel must be hidden on mobile devices as tile customization is a desktop-only feature.

**FR-7: Mobile Navigation Notice**
Non-dashboard pages must display a dismissible notice informing users that the page is optimized for desktop use.

**FR-8: Viewport Configuration**
The application must include proper viewport meta tags to ensure correct scaling on mobile devices.

**FR-9: Sidebar Overlay**
The sidebar navigation must function as an overlay with backdrop on mobile devices, triggered by a hamburger menu.

**FR-10: Read-Only Dashboard**
Mobile dashboard is read-only - users can view data and navigate via clickable tiles but cannot customize, reorder, or configure tiles.

## Non-Goals (Out of Scope)

- Full mobile responsiveness for all application pages (only dashboard is optimized)
- Touch gestures like swipe, pinch-to-zoom, or pull-to-refresh
- Mobile-specific data refresh rates or performance optimizations
- Offline functionality or progressive web app (PWA) features
- Mobile-specific tile types or data visualizations
- Tablet-specific layouts (tablets will use desktop layout)
- Mobile app versions (iOS/Android native apps)
- Push notifications for mobile users
- Mobile-specific authentication or session handling

## Design Considerations

### UI/UX Requirements

**Dashboard Grid Layout:**
- **Mobile (<768px)**: Single column, full-width tiles
- **Tablet (768px-1023px)**: Desktop layout (multi-column)
- **Desktop (≥1024px)**: Current layout unchanged

**Tile Sizing:**
- All tile sizes (Small, Medium, Large) render as full-width on mobile
- Tiles maintain aspect ratios where applicable (charts, graphics)
- Minimum tile height: 120px (for readability)
- Maximum tile height: Unlimited (content-dependent)

**Touch Interactions:**
- Minimum tap target: 44px × 44px (Apple HIG, Material Design guidelines)
- Touch feedback: Active state with background color change
- No hover states on mobile (replaced with active states)
- Fast tap response (no 300ms delay)

**Typography:**
- Tile titles: 16px-18px (desktop: 20px-24px)
- Count numbers: 24px-32px (desktop: 36px-48px)
- Body text: 14px minimum (desktop: 14px-16px)
- Line height: 1.5 minimum for readability

**Spacing:**
- Tile gap: 16px (desktop: 16px)
- Container padding: 16px (desktop: 24px)
- Header padding: 16px (desktop: 24px)

### Tile-Specific Mobile Optimizations

**Count Tiles:**
- Icon size: 32px-40px (desktop: 40px-48px)
- Number font size: 28px-32px
- Label font size: 14px

**Chart Tiles (Production, Inventory Summary):**
- Chart height: 200px-300px (responsive)
- Axis labels: 12px minimum
- Legend: Positioned below chart, not beside

**List Tiles (Low Stock Alert, Background Tasks):**
- List items: Full-width, adequate padding (12px vertical)
- Item font size: 14px
- Maximum visible items: 5 (with "Show more" link)

**Mobile Notice Component:**
- Background: `bg-yellow-50 border-l-4 border-yellow-400`
- Icon: `AlertCircle` from Lucide React
- Text: 14px, `text-yellow-800`
- Padding: 12px
- Position: Fixed at top, full-width

### Reference Existing Components

- Dashboard grid: `/frontend/src/components/dashboard/DashboardGrid.tsx`
- Dashboard tiles: `/frontend/src/components/dashboard/DashboardTile.tsx`
- Tile content: `/frontend/src/components/dashboard/tiles/TileContent.tsx`
- Tile types: All tile components in `/frontend/src/components/dashboard/tiles/`
- Sidebar: `/frontend/src/components/Layout/Sidebar.tsx`

## Technical Considerations

### Current Architecture
- **React 18** with TypeScript
- **Tailwind CSS** for styling (utility-first approach)
- **@dnd-kit** for drag-and-drop (desktop only)
- **Responsive breakpoints**: Tailwind default (`sm: 640px`, `md: 768px`, `lg: 1024px`, `xl: 1280px`)

### Mobile Breakpoint Strategy
- Use `md:` prefix for desktop-only styles (≥768px)
- Mobile styles are default (mobile-first approach)
- Example: `grid-cols-1 md:grid-cols-3 lg:grid-cols-6`

### Conditional Rendering
Use React hooks to detect mobile screens:
```typescript
const isMobile = useMediaQuery('(max-width: 767px)');
```

Conditionally disable drag-and-drop:
```typescript
{!isMobile && <DndContext>...</DndContext>}
{isMobile && <div>Static tiles</div>}
```

### Performance Considerations
- Mobile devices have less processing power - avoid heavy JavaScript on mobile
- Disable drag-and-drop reduces JavaScript overhead
- Use CSS transforms for smooth scrolling (GPU-accelerated)
- Lazy load tile data if performance issues arise

### Browser Compatibility
- **iOS Safari 14+** (iPhone 11 and newer)
- **Android Chrome 90+** (Android 10 and newer)
- **Samsung Internet 14+**

### Testing Strategy
- **Unit Tests**: Test mobile breakpoint logic, conditional rendering
- **Integration Tests**: Test tile rendering on different screen sizes
- **E2E Tests**: Test dashboard on mobile viewports using Playwright with device emulation
- **Manual Testing**: Test on real iOS and Android devices

### Accessibility
- Touch targets meet minimum size requirements (44px)
- Text meets contrast ratio requirements (WCAG 2.1 AA)
- Semantic HTML for screen readers
- Focus indicators for keyboard navigation (even on mobile)

## Success Metrics

**User Experience:**
- Mobile users can view all dashboard tiles without horizontal scrolling
- 100% of dashboard tiles render correctly on mobile screens (320px-428px wide)
- Touch interactions respond within 100ms (perceived as instant)
- Dashboard loads within 3 seconds on 4G mobile connection

**Technical Metrics:**
- Zero layout shift (CLS) when switching to mobile
- No console errors on mobile browsers
- Page weight under 500KB (uncompressed) for mobile dashboard
- Lighthouse mobile score: ≥90 for Performance, ≥95 for Accessibility

**Quality Metrics:**
- All mobile-specific user stories have passing tests
- Visual regression tests pass for mobile breakpoints
- No accessibility violations detected by automated tools

## Open Questions

1. **Dashboard refresh on mobile**: Should mobile users have manual pull-to-refresh, or keep automatic 30-second refresh?
   - *Recommendation*: Keep automatic refresh but add visual indicator when data updates

2. **Tile order on mobile**: Should tile order differ on mobile (e.g., most important tiles first)?
   - *Recommendation*: Use same order as desktop for consistency

3. **Mobile landscape mode**: Should we optimize for landscape orientation on mobile?
   - *Recommendation*: No special handling - landscape uses same single-column layout

4. **Deep linking to tiles**: Should we support URL fragments to scroll to specific tiles on mobile?
   - *Recommendation*: Out of scope for MVP, add in future iteration if requested

5. **Mobile sidebar persistence**: Should sidebar remember open/closed state on mobile?
   - *Recommendation*: No - always closed by default on mobile for clean experience

---

## Implementation Notes

### Phase 1: Core Mobile Layout (US-001, US-002, US-006, US-007)
Focus on basic mobile grid, disabling drag-and-drop, header optimization, and viewport configuration.

### Phase 2: Mobile Interactions (US-003, US-004, US-005)
Optimize tile content, touch targets, and hide unnecessary features.

### Phase 3: Navigation & Notices (US-008, US-009)
Implement mobile sidebar overlay and desktop-optimized page notices.

### Phase 4: Testing & Validation (US-010)
Comprehensive testing on real devices and documentation.

### Design Document Alignment
- **UI Design Document**: `/docs/design/ui_design_document.md` - Follow existing color palette, spacing, and component styles
- **Layout Definition**: `/docs/design/layout_definition.md` - Adapt desktop layout rules for mobile constraints
- **Mobile Responsiveness Principle**: The UI design document states "Mobilní připravenost – layout se přizpůsobuje menším zařízením" - this PRD implements that principle

### File Changes Required
- `/frontend/src/components/pages/Dashboard.tsx` - Add mobile breakpoint detection, conditional rendering
- `/frontend/src/components/dashboard/DashboardGrid.tsx` - Update grid classes for mobile
- `/frontend/src/components/dashboard/DashboardTile.tsx` - Disable drag-and-drop on mobile
- `/frontend/src/components/dashboard/tiles/*.tsx` - Optimize each tile type for mobile
- `/frontend/src/components/Layout/Sidebar.tsx` - Implement mobile overlay behavior
- `/frontend/src/components/common/MobileNotice.tsx` - New component for non-dashboard pages
- `/frontend/public/index.html` - Update viewport meta tag
- `/frontend/src/hooks/useMediaQuery.ts` - New hook for mobile detection (if not exists)
